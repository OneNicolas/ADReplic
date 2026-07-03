using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détection croisée DNS/Ports (backlog 5.2) : signale les enregistrements SRV
    /// qui pointent vers un hôte dont les ports AD critiques (389 LDAP, 88 Kerberos)
    /// sont tous inaccessibles. Cas typique : DC retiré brutalement (sans dcpromo)
    /// qui laisse des SRV orphelins en zone — la résolution DNS est Ok, mais les
    /// clients qui suivent ce SRV tombent dans le vide.
    ///
    /// Un hôte n'est considéré "mort" que si TOUS ses ports critiques testés sont
    /// en Closed/Timeout : un seul port fermé évoque plutôt un pare-feu, cas déjà
    /// couvert par PORT_CRITICAL_CLOSED. Sévérité Warning (et pas Critical) parce
    /// que l'indisponibilité de l'hôte lui-même est déjà remontée en Critical
    /// ailleurs ; ici on signale l'incohérence DNS résiduelle à nettoyer.
    ///
    /// Silencieux si l'une des deux sondes (DNS ou Ports) n'a pas été exécutée.
    /// </summary>
    public sealed class DnsSrvPointsToDeadDcDetector : IIssueDetector
    {
        private static readonly HashSet<int> CriticalPorts = new HashSet<int> { 389, 88 };

        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DnsHealth?.Checks == null) yield break;
            if (snapshot.PortHealth?.Checks == null) yield break;

            var deadHosts = BuildDeadHostSet(snapshot.PortHealth.Checks);
            if (deadHosts.Count == 0) yield break;

            // Groupé par hôte mort : un DC down porte typiquement 3-4 SRV, une issue
            // par SRV noierait le Diagnostics. AffectedItems liste chaque record.
            var orphanSrvByHost = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var check in snapshot.DnsHealth.Checks)
            {
                if (!IsSrv(check.Type)) continue;
                if (check.Status != DnsCheckStatus.Ok) continue;

                var target = NormalizeHost(check.Target);
                if (target == null || !deadHosts.Contains(target)) continue;

                if (!orphanSrvByHost.TryGetValue(target, out var records))
                {
                    records = new List<string>();
                    orphanSrvByHost[target] = records;
                }
                records.Add(check.RecordName);
            }

            foreach (var pair in orphanSrvByHost.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                yield return new DetectedIssue
                {
                    Code        = "DNS_SRV_POINTS_TO_DEAD_DC",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Enregistrements SRV pointant vers un DC injoignable : {pair.Key}",
                    Description =
                        "Ces enregistrements SRV se résolvent correctement mais leur cible " +
                        "ne répond sur aucun port AD critique (LDAP 389, Kerberos 88). " +
                        "Les clients qui sélectionnent ce SRV subiront des délais ou des " +
                        "échecs de localisation de DC. Si l'hôte est définitivement retiré, " +
                        "supprimez ses enregistrements de la zone DNS (ou lancez un " +
                        "metadata cleanup) ; s'il est simplement down, restaurez-le.",
                    AffectedItems = pair.Value
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
            }
        }

        private static HashSet<string> BuildDeadHostSet(IReadOnlyList<PortCheckResult> checks)
        {
            var criticalChecksByHost = new Dictionary<string, List<PortCheckResult>>(StringComparer.OrdinalIgnoreCase);
            foreach (var check in checks)
            {
                if (check == null || !CriticalPorts.Contains(check.Port)) continue;
                var host = NormalizeHost(check.HostName);
                if (host == null) continue;

                if (!criticalChecksByHost.TryGetValue(host, out var list))
                {
                    list = new List<PortCheckResult>();
                    criticalChecksByHost[host] = list;
                }
                list.Add(check);
            }

            var dead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in criticalChecksByHost)
            {
                if (pair.Value.All(IsInaccessible)) dead.Add(pair.Key);
            }
            return dead;
        }

        private static bool IsInaccessible(PortCheckResult check) =>
            check.Status == PortCheckStatus.Closed || check.Status == PortCheckStatus.Timeout;

        private static bool IsSrv(DnsCheckedRecordType type) =>
            type == DnsCheckedRecordType.SrvLdap
            || type == DnsCheckedRecordType.SrvKerberos
            || type == DnsCheckedRecordType.SrvGc
            || type == DnsCheckedRecordType.SrvKpasswd;

        /// <summary>
        /// Normalise un nom d'hôte pour la comparaison SRV target vs port check :
        /// les cibles SRV issues du DNS portent souvent un point final (FQDN absolu)
        /// que les noms de l'inventaire n'ont pas.
        /// </summary>
        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return null;
            var normalized = host.Trim().TrimEnd('.');
            return normalized.Length == 0 ? null : normalized;
        }
    }
}
