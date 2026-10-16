using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Rédige la phrase de synthèse d'un audit, en français clair, pour un
    /// lecteur non expert. Ordre des faits : DC injoignables (l'état réel est
    /// inconnu), puis échecs de réplication, puis anomalies de configuration.
    /// Aucun code, aucun GUID : les détails restent dans les onglets.
    /// </summary>
    public static class AuditSummaryWriter
    {
        /// <summary>
        /// Phrase neutre quand l'audit s'est arrêté avant d'avoir collecté quoi que ce
        /// soit (erreur, délai dépassé). Sans elle, des onglets vides ressemblent à une
        /// forêt saine ; la phrase dit explicitement que ce n'est pas le cas.
        /// </summary>
        public static string WriteNotCompleted(string explanation, bool partialData = false)
        {
            var head = string.IsNullOrWhiteSpace(explanation)
                ? "L'audit n'a pas abouti."
                : "L'audit n'a pas abouti : " + explanation.Trim();
            if (!head.EndsWith(".")) head += ".";
            return partialData
                ? head + " Les données affichées sont partielles : aucun score ni diagnostic n'a pu être calculé."
                : head + " Aucune donnée n'a été collectée : les onglets vides ne reflètent pas l'état de la forêt.";
        }

        public static string Write(AuditSnapshot snapshot)
        {
            if (snapshot == null) return "";

            var dcs      = snapshot.DomainControllers ?? new List<DomainControllerInfo>();
            var failures = snapshot.ReplicationFailures ?? new List<ReplicationFailureInfo>();
            var issues   = snapshot.Issues ?? new List<DetectedIssue>();

            var links    = snapshot.ReplicationLinks ?? new List<ReplicationLink>();

            var dcCount          = dcs.Count;
            var unreachableCount = dcs.Count(d => !d.IsReachable);
            var scope            = snapshot.IsSingleDcMode ? " (mode DC seul)" : "";

            // La matrice peut être rouge sans « échec actif » ni anomalie : juste après le
            // redémarrage d'un DC, ses liens portent encore la dernière tentative ratée
            // alors que GetReplicationConnectionFailures ne remonte plus rien. Le score
            // compte ces liens ; la phrase doit le faire aussi, sinon elle dit
            // « Tout fonctionne » sous un bandeau rouge. Les liens d'un DC injoignable
            // sont déjà couverts par la phrase sur les DC injoignables.
            var unreachableHosts = new HashSet<string>(
                dcs.Where(d => !d.IsReachable).Select(d => d.HostName ?? ""), StringComparer.OrdinalIgnoreCase);
            var failingLinks = links.Count(l =>
                (l.Status == ReplicationLinkStatus.Failing || l.Status == ReplicationLinkStatus.Unreachable)
                && !unreachableHosts.Contains(l.DestinationDc ?? ""));
            var warningLinks = links.Count(l => l.Status == ReplicationLinkStatus.Warning);

            if (unreachableCount == 0 && failures.Count == 0 && issues.Count == 0 && failingLinks == 0 && warningLinks == 0)
            {
                return $"Tout fonctionne : {Plural(dcCount, "contrôleur de domaine", "contrôleurs de domaine")}{scope}, " +
                       "aucun échec de réplication, aucune anomalie de configuration.";
            }

            var parts = new List<string>();

            if (unreachableCount > 0)
            {
                parts.Add(unreachableCount == 1
                    ? $"1 contrôleur de domaine sur {dcCount} est injoignable (son état réel est inconnu)"
                    : $"{unreachableCount} contrôleurs de domaine sur {dcCount} sont injoignables (leur état réel est inconnu)");
            }

            // Les échecs synthétiques (code -1 : "DC injoignable") sont déjà couverts
            // par la phrase sur les DC injoignables : les recompter serait redondant.
            var realFailures = failures.Where(f => f.LastErrorCode != ReplicationFailureProbe.UnreachableErrorCode).ToList();
            if (realFailures.Count > 0) parts.Add(DescribeFailures(realFailures));

            if (failingLinks > 0)
                parts.Add($"{Plural(failingLinks, "lien de réplication", "liens de réplication")} sur {links.Count} " +
                          "en échec à la dernière tentative");
            if (warningLinks > 0)
                parts.Add($"{Plural(warningLinks, "lien en avertissement", "liens en avertissement")}");

            // Le DC injoignable et ses conséquences (ports, SRV orphelins) sont déjà
            // couverts par la phrase ci-dessus, et REPLICATION_LINK_FAILING décrit le
            // même fait que le compteur de liens en échec : on ne compte que les
            // anomalies de configuration à proprement parler.
            var standaloneIssues = IssueGrouper.TopLevelExcludingUnreachable(issues)
                .Where(i => i.Code != ReplicationLinkFailingDetector.Code)
                .ToList();
            if (standaloneIssues.Count > 0) parts.Add(DescribeIssues(standaloneIssues));

            var sentence = string.Join(" ; ", parts) + ".";
            return snapshot.IsSingleDcMode ? sentence + " Audit en mode DC seul." : sentence;
        }

        private static string DescribeFailures(IReadOnlyList<ReplicationFailureInfo> failures)
        {
            if (failures.Count == 1)
            {
                var f = failures[0];
                return $"1 échec de réplication depuis {DurationFormatter.ToHumanText(f.FailureDuration)} " +
                       $"entre {f.SourceDc} et {f.DestinationDc} ({LowerFirst(f.ErrorTitle)})";
            }

            var oldest = failures.OrderBy(f => f.FirstFailureTime).First();
            return $"{failures.Count} échecs de réplication, le plus ancien depuis " +
                   $"{DurationFormatter.ToHumanText(oldest.FailureDuration)} ({LowerFirst(oldest.ErrorTitle)})";
        }

        private static string DescribeIssues(IReadOnlyList<DetectedIssue> issues)
        {
            var critical = issues.Count(i => i.Severity == IssueSeverity.Critical);
            var text = Plural(issues.Count, "anomalie de configuration à traiter", "anomalies de configuration à traiter");
            if (critical > 0) text += $" dont {Plural(critical, "critique", "critiques")}";
            return text;
        }

        private static string Plural(int count, string singular, string plural)
            => count + " " + (count > 1 ? plural : singular);

        /// <summary>Abaisse l'initiale pour insérer un titre en milieu de phrase, sauf pour un sigle ("DC", "RPC").</summary>
        private static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length > 1 && char.IsUpper(s[1])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}
