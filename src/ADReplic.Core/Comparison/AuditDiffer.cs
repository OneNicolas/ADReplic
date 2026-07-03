using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Calcule un <see cref="AuditDiff"/> entre deux <see cref="AuditSnapshot"/>.
    /// Helper statique sans état, idempotent : appeler Compute(A, B) puis Compute(A, B)
    /// donne deux résultats égaux structurellement.
    /// </summary>
    /// <remarks>
    /// Le diff est volontairement exhaustif (toutes les catégories sont comparées
    /// même si l'utilisateur ne s'intéresse qu'au score). C'est la responsabilité
    /// de <see cref="SignificantChangeDetector"/> d'en extraire l'essentiel pour
    /// le système d'alerte.
    /// </remarks>
    public static class AuditDiffer
    {
        public static AuditDiff Compute(AuditSnapshot reference, AuditSnapshot current)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (current == null) throw new ArgumentNullException(nameof(current));

            return new AuditDiff
            {
                ReferenceGeneratedAt = reference.GeneratedAt,
                CurrentGeneratedAt = current.GeneratedAt,
                ReferenceForestName = reference.ForestName,
                CurrentForestName = current.ForestName,

                DomainControllers = ComputeDelta(
                    reference.DomainControllers, current.DomainControllers,
                    DcKey, DcHasChanged),

                ReplicationLinks = ComputeDelta(
                    reference.ReplicationLinks, current.ReplicationLinks,
                    LinkKey, LinkHasChanged),

                ReplicationFailures = ComputeDelta(
                    reference.ReplicationFailures, current.ReplicationFailures,
                    FailureKey, FailureHasChanged),

                Sites = ComputeDelta(
                    reference.Sites, current.Sites,
                    SiteKey, SiteHasChanged),

                SiteLinks = ComputeDelta(
                    reference.SiteLinks, current.SiteLinks,
                    SiteLinkKey, SiteLinkHasChanged),

                Issues = ComputeDelta(
                    reference.Issues, current.Issues,
                    IssueKey, NeverChanged),

                DnsChecks = ComputeDelta(
                    reference.DnsHealth?.Checks, current.DnsHealth?.Checks,
                    DnsKey, DnsHasChanged),

                PortChecks = ComputeDelta(
                    reference.PortHealth?.Checks, current.PortHealth?.Checks,
                    PortKey, PortHasChanged),

                Score = ComputeScoreDelta(reference.HealthScore, current.HealthScore)
            };
        }

        // === Coeur de l'algorithme : comparaison de deux listes via clé d'identité ===

        private static SnapshotDelta<T> ComputeDelta<T>(
            IReadOnlyList<T> before,
            IReadOnlyList<T> after,
            Func<T, string> keyOf,
            Func<T, T, bool> hasChanged) where T : class
        {
            var beforeMap = ToMap(before, keyOf);
            var afterMap = ToMap(after, keyOf);

            var added = new List<T>();
            var removed = new List<T>();
            var changed = new List<DeltaChange<T>>();

            foreach (var pair in afterMap)
            {
                if (!beforeMap.TryGetValue(pair.Key, out var beforeItem))
                {
                    added.Add(pair.Value);
                }
                else if (hasChanged(beforeItem, pair.Value))
                {
                    changed.Add(new DeltaChange<T>
                    {
                        Key = pair.Key,
                        Before = beforeItem,
                        After = pair.Value
                    });
                }
            }

            foreach (var pair in beforeMap)
            {
                if (!afterMap.ContainsKey(pair.Key))
                {
                    removed.Add(pair.Value);
                }
            }

            return new SnapshotDelta<T>
            {
                Added = added,
                Removed = removed,
                Changed = changed
            };
        }

        private static Dictionary<string, T> ToMap<T>(
            IReadOnlyList<T> items, Func<T, string> keyOf) where T : class
        {
            var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            if (items == null) return map;

            foreach (var item in items)
            {
                if (item == null) continue;
                var key = keyOf(item);
                if (string.IsNullOrEmpty(key)) continue;
                // Les modèles AD ne devraient pas produire de doublons par clé,
                // mais on reste défensif : premier rencontré gagne.
                if (!map.ContainsKey(key)) map.Add(key, item);
            }
            return map;
        }

        // === Extracteurs de clé d'identité par type ===

        private static string DcKey(DomainControllerInfo dc) =>
            dc.HostName ?? string.Empty;

        private static string LinkKey(ReplicationLink l) =>
            $"{l.DestinationDc}|{l.SourceDc}|{l.NamingContext}";

        private static string FailureKey(ReplicationFailureInfo f) =>
            $"{f.DestinationDc}|{f.SourceDc}";

        private static string SiteKey(SiteInfo s) =>
            s.Name ?? string.Empty;

        private static string SiteLinkKey(SiteLinkInfo sl) =>
            sl.Name ?? string.Empty;

        private static string IssueKey(DetectedIssue i)
        {
            // Deux issues avec le même code mais sur des éléments différents
            // (ex : DC_ISOLATED sur DC01 vs DC02) sont distinctes. On inclut donc
            // les AffectedItems triés pour stabilité de la clé.
            var items = (i.AffectedItems ?? new List<string>())
                .OrderBy(x => x ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            return $"{i.Code}|{string.Join(",", items)}";
        }

        private static string DnsKey(DnsCheckResult d) =>
            $"{d.RecordName}|{(int)d.Type}|{d.Target ?? string.Empty}";

        private static string PortKey(PortCheckResult p) =>
            $"{p.HostName}|{p.Port}";

        // === Prédicats "a changé" par type ===

        private static bool DcHasChanged(DomainControllerInfo a, DomainControllerInfo b)
        {
            // DiscoveredAt change toujours entre deux passes : ne pas le comparer.
            // IsReachable comparé depuis v0.6.0 : un DC qui bascule joignable <->
            // injoignable doit apparaître dans les "Modifs" du diff même si ses
            // autres champs (vidés par la panne) ne suffisent pas à le signaler.
            return !StringEq(a.OSVersion, b.OSVersion)
                || !StringEq(a.SiteName, b.SiteName)
                || !StringEq(a.IPAddress, b.IPAddress)
                || a.IsGlobalCatalog != b.IsGlobalCatalog
                || a.IsReadOnly != b.IsReadOnly
                || a.IsReachable != b.IsReachable;
        }

        private static bool LinkHasChanged(ReplicationLink a, ReplicationLink b) =>
            a.Status != b.Status;

        private static bool FailureHasChanged(ReplicationFailureInfo a, ReplicationFailureInfo b)
        {
            // FirstFailureTime peut bouger légèrement à cause de la fenêtre d'audit.
            // Le code d'erreur, la sévérité et le compteur sont les signaux pertinents.
            return a.LastErrorCode != b.LastErrorCode
                || a.Severity != b.Severity
                || a.ConsecutiveFailureCount != b.ConsecutiveFailureCount;
        }

        private static bool SiteHasChanged(SiteInfo a, SiteInfo b)
        {
            return !ListEq(a.DomainControllers, b.DomainControllers)
                || !ListEq(a.Subnets, b.Subnets)
                || !ListEq(a.BridgeheadServers, b.BridgeheadServers)
                || !StringEq(a.Location, b.Location);
        }

        private static bool SiteLinkHasChanged(SiteLinkInfo a, SiteLinkInfo b)
        {
            return a.Cost != b.Cost
                || a.ReplicationInterval != b.ReplicationInterval
                || a.ChangeNotificationEnabled != b.ChangeNotificationEnabled
                || !StringEq(a.TransportType, b.TransportType)
                || !ListEq(a.Sites, b.Sites);
        }

        private static bool DnsHasChanged(DnsCheckResult a, DnsCheckResult b) =>
            a.Status != b.Status;

        private static bool PortHasChanged(PortCheckResult a, PortCheckResult b) =>
            a.Status != b.Status;

        private static bool NeverChanged<T>(T a, T b) => false;

        private static HealthScoreDelta ComputeScoreDelta(HealthScore before, HealthScore after)
        {
            if (before == null || after == null) return null;
            return new HealthScoreDelta
            {
                ValueBefore = before.Value,
                ValueAfter = after.Value,
                LevelBefore = before.Level,
                LevelAfter = after.Level,
                ReplicationScoreBefore = before.ReplicationScore,
                ReplicationScoreAfter = after.ReplicationScore,
                DnsScoreBefore = before.DnsScore,
                DnsScoreAfter = after.DnsScore,
                PortScoreBefore = before.PortScore,
                PortScoreAfter = after.PortScore
            };
        }

        // === Helpers de comparaison ===

        private static bool StringEq(string a, string b) =>
            string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        private static bool ListEq(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            var aa = a ?? (IReadOnlyList<string>)Array.Empty<string>();
            var bb = b ?? (IReadOnlyList<string>)Array.Empty<string>();
            if (aa.Count != bb.Count) return false;

            // Ordre stable indépendamment de l'ordre source : on compare comme des sets.
            var sortedA = aa.OrderBy(x => x ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
            var sortedB = bb.OrderBy(x => x ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < sortedA.Count; i++)
            {
                if (!StringEq(sortedA[i], sortedB[i])) return false;
            }
            return true;
        }
    }
}
