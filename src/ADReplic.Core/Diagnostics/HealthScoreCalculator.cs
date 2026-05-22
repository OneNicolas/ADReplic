using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Calcule un score de santé 0-100 à partir des données d'audit.
    /// Score tripartite : Réplication 50 % / DNS 25 % / Réseau (ports) 25 %.
    ///
    /// Renormalisation : si une sonde (DNS ou Ports) n'a pas été exécutée,
    /// ses poids sont redistribués sur les catégories disponibles. Cas limite
    /// "réplication seule" → poids 100 % réplication, ce qui préserve le
    /// comportement historique de l'outil quand les sondes ne sont pas
    /// encore branchées côté GUI/PowerShell.
    /// </summary>
    public static class HealthScoreCalculator
    {
        // Pondérations cibles du score global.
        private const double WeightReplication = 0.5;
        private const double WeightDns = 0.25;
        private const double WeightPorts = 0.25;

        // Pénalités catégorie Réplication (inchangées par rapport à la v0.3.0).
        private const int FailingLinkPenalty = 25;
        private const int WarningLinkPenalty = 5;
        private const int ActiveFailurePenalty = 10;

        // Pénalités catégorie DNS.
        private const int DnsCriticalSrvMissingPenalty = 25;   // _ldap, _kerberos absents
        private const int DnsOptionalSrvMissingPenalty = 10;   // _gc, _kpasswd absents
        private const int DnsErrorPenalty = 15;                // SERVFAIL, NO_DNS_SERVERS, etc.
        private const int DnsDcARecordMissingPenalty = 20;     // un DC sans A record résolvable

        // Pénalités catégorie Réseau (ports).
        private const int CriticalPortBlockedPenalty = 25;     // 389, 88, 445 fermé OU en timeout
        private const int OtherPortClosedPenalty = 5;
        private const int OtherPortTimeoutPenalty = 5;
        private const int PortErrorPenalty = 3;

        // Ports dont l'inaccessibilité brise immédiatement les services AD principaux.
        private static readonly HashSet<int> CriticalPorts = new HashSet<int> { 389, 88, 445 };

        public static HealthScore Compute(AuditSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var anomalies = new List<string>();

            int replicationScore = ComputeReplicationScore(snapshot, anomalies);
            int? dnsScore = snapshot.DnsHealth != null
                ? ComputeDnsScore(snapshot.DnsHealth, anomalies)
                : (int?)null;
            int? portScore = snapshot.PortHealth != null
                ? ComputePortScore(snapshot.PortHealth, anomalies)
                : (int?)null;

            int globalScore = ComputeWeightedGlobal(replicationScore, dnsScore, portScore);
            var level = ClassifyLevel(globalScore);

            return new HealthScore
            {
                Value = globalScore,
                Level = level,
                Label = LabelFor(level),
                Summary = BuildSummary(anomalies),
                Anomalies = anomalies,
                ReplicationScore = replicationScore,
                DnsScore = dnsScore,
                PortScore = portScore
            };
        }

        // -- Sous-score Réplication -----------------------------------------------

        private static int ComputeReplicationScore(AuditSnapshot snapshot, List<string> anomalies)
        {
            int score = 100;

            var failingLinks = snapshot.ReplicationLinks?
                .Count(l => l.Status == ReplicationLinkStatus.Failing ||
                            l.Status == ReplicationLinkStatus.Unreachable) ?? 0;
            if (failingLinks > 0)
            {
                score -= failingLinks * FailingLinkPenalty;
                anomalies.Add($"{failingLinks} lien(s) en échec ou injoignable(s)");
            }

            var warningLinks = snapshot.ReplicationLinks?
                .Count(l => l.Status == ReplicationLinkStatus.Warning) ?? 0;
            if (warningLinks > 0)
            {
                score -= warningLinks * WarningLinkPenalty;
                anomalies.Add($"{warningLinks} lien(s) en avertissement");
            }

            var activeFailures = snapshot.ReplicationFailures?.Count ?? 0;
            if (activeFailures > 0)
            {
                score -= activeFailures * ActiveFailurePenalty;
                anomalies.Add($"{activeFailures} échec(s) de réplication actifs");
            }

            return Math.Max(0, score);
        }

        // -- Sous-score DNS -------------------------------------------------------

        private static int ComputeDnsScore(DnsHealthResult dns, List<string> anomalies)
        {
            int score = 100;
            int criticalMissing = 0, optionalMissing = 0, errors = 0, dcARecordMissing = 0;

            if (dns.Checks != null)
            {
                foreach (var check in dns.Checks)
                {
                    if (check.Status == DnsCheckStatus.Missing)
                    {
                        switch (check.Type)
                        {
                            case DnsCheckedRecordType.SrvLdap:
                            case DnsCheckedRecordType.SrvKerberos:
                                criticalMissing++;
                                break;
                            case DnsCheckedRecordType.SrvGc:
                            case DnsCheckedRecordType.SrvKpasswd:
                                optionalMissing++;
                                break;
                            case DnsCheckedRecordType.ARecord:
                                dcARecordMissing++;
                                break;
                        }
                    }
                    else if (check.Status == DnsCheckStatus.Error)
                    {
                        errors++;
                    }
                }
            }

            score -= criticalMissing * DnsCriticalSrvMissingPenalty;
            score -= optionalMissing * DnsOptionalSrvMissingPenalty;
            score -= errors * DnsErrorPenalty;
            score -= dcARecordMissing * DnsDcARecordMissingPenalty;

            if (criticalMissing > 0)
                anomalies.Add($"{criticalMissing} enregistrement(s) SRV critique(s) DNS manquant(s)");
            if (optionalMissing > 0)
                anomalies.Add($"{optionalMissing} enregistrement(s) SRV optionnel(s) DNS manquant(s)");
            if (errors > 0)
                anomalies.Add($"{errors} erreur(s) de résolution DNS");
            if (dcARecordMissing > 0)
                anomalies.Add($"{dcARecordMissing} DC sans enregistrement A résolvable");

            return Math.Max(0, score);
        }

        // -- Sous-score Réseau ----------------------------------------------------

        private static int ComputePortScore(PortHealthResult ports, List<string> anomalies)
        {
            int score = 100;
            int criticalBlocked = 0, otherClosed = 0, timeouts = 0, errors = 0;

            if (ports.Checks != null)
            {
                foreach (var check in ports.Checks)
                {
                    var isCritical = CriticalPorts.Contains(check.Port);
                    switch (check.Status)
                    {
                        case PortCheckStatus.Closed:
                            if (isCritical) criticalBlocked++;
                            else otherClosed++;
                            break;
                        case PortCheckStatus.Timeout:
                            // Sur un port critique, un timeout est aussi grave qu'un closed
                            // (le service ne répond pas, peu importe la cause).
                            if (isCritical) criticalBlocked++;
                            else timeouts++;
                            break;
                        case PortCheckStatus.Error:
                            errors++;
                            break;
                    }
                }
            }

            score -= criticalBlocked * CriticalPortBlockedPenalty;
            score -= otherClosed * OtherPortClosedPenalty;
            score -= timeouts * OtherPortTimeoutPenalty;
            score -= errors * PortErrorPenalty;

            if (criticalBlocked > 0)
                anomalies.Add($"{criticalBlocked} port(s) critique(s) inaccessible(s) (LDAP/Kerberos/SMB)");
            if (otherClosed > 0)
                anomalies.Add($"{otherClosed} autre(s) port(s) fermé(s)");
            if (timeouts > 0)
                anomalies.Add($"{timeouts} port(s) en timeout");
            if (errors > 0)
                anomalies.Add($"{errors} erreur(s) sur tests de ports");

            return Math.Max(0, score);
        }

        // -- Agrégation pondérée avec renormalisation -----------------------------

        private static int ComputeWeightedGlobal(int replicationScore, int? dnsScore, int? portScore)
        {
            double totalWeight = WeightReplication;
            double weightedSum = replicationScore * WeightReplication;

            if (dnsScore.HasValue)
            {
                totalWeight += WeightDns;
                weightedSum += dnsScore.Value * WeightDns;
            }

            if (portScore.HasValue)
            {
                totalWeight += WeightPorts;
                weightedSum += portScore.Value * WeightPorts;
            }

            // Division par totalWeight = renormalisation des poids effectifs à 1.0.
            // Quand seul Replication est présent : totalWeight = 0.5 → global = score
            // de réplication brut (préserve le comportement v0.3.0 et les tests existants).
            return (int)Math.Round(weightedSum / totalWeight);
        }

        private static HealthLevel ClassifyLevel(int score)
        {
            if (score >= 90) return HealthLevel.Excellent;
            if (score >= 60) return HealthLevel.Warning;
            return HealthLevel.Critical;
        }

        private static string LabelFor(HealthLevel level)
        {
            switch (level)
            {
                case HealthLevel.Excellent: return "Excellent";
                case HealthLevel.Warning:   return "Avertissements";
                case HealthLevel.Critical:  return "Critique";
                default: return "Inconnu";
            }
        }

        private static string BuildSummary(IReadOnlyList<string> anomalies)
        {
            if (anomalies.Count == 0) return "Aucune anomalie détectée.";
            return string.Join(", ", anomalies) + ".";
        }
    }
}
