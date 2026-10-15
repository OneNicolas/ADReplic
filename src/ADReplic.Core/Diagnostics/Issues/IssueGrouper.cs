using System;
using System.Collections.Generic;
using System.Linq;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Agrégation de présentation : un DC injoignable produit à lui seul une dizaine
    /// d'anomalies (ports fermés, SRV orphelins) qui noient les autres constats.
    /// Ce regroupement replie ces conséquences derrière leur DC_UNREACHABLE.
    ///
    /// Fonction pure sur la liste plate : l'ordre des parents et des enfants est
    /// celui de l'entrée (sévérité décroissante puis code, garanti par
    /// <see cref="IssueAggregator"/>), aucune anomalie n'est perdue ni dupliquée.
    /// Une anomalie n'est rattachée QUE si un DC_UNREACHABLE existe pour le même
    /// hôte : un port fermé sur un DC qui répond reste au premier niveau, car il
    /// a une cause propre (pare-feu) qui mérite d'être vue.
    /// </summary>
    public static class IssueGrouper
    {
        public const string UnreachableCode = "DC_UNREACHABLE";

        public static IReadOnlyList<IssueGroup> Group(IReadOnlyList<DetectedIssue> issues)
        {
            if (issues == null || issues.Count == 0) return Array.Empty<IssueGroup>();

            var unreachableHosts = new HashSet<string>(
                issues.Where(IsUnreachableWithHost).Select(i => NormalizeHost(i.RelatedHost)),
                StringComparer.OrdinalIgnoreCase);

            var childrenByHost = new Dictionary<string, List<DetectedIssue>>(StringComparer.OrdinalIgnoreCase);
            var parents = new List<DetectedIssue>();

            foreach (var issue in issues)
            {
                if (issue == null) continue;
                if (IsDerivedFromUnreachableDc(issue, unreachableHosts))
                {
                    var host = NormalizeHost(issue.RelatedHost);
                    if (!childrenByHost.TryGetValue(host, out var list))
                    {
                        list = new List<DetectedIssue>();
                        childrenByHost[host] = list;
                    }
                    list.Add(issue);
                }
                else
                {
                    parents.Add(issue);
                }
            }

            return parents
                .Select(p => new IssueGroup(p, ChildrenOf(p, childrenByHost)))
                .ToList();
        }

        /// <summary>
        /// Anomalies à compter dans une synthèse : les parents hors DC_UNREACHABLE.
        /// Le DC injoignable et ses conséquences sont déjà résumés par la phrase
        /// « N contrôleurs injoignables ».
        /// </summary>
        public static IReadOnlyList<DetectedIssue> TopLevelExcludingUnreachable(IReadOnlyList<DetectedIssue> issues)
            => Group(issues)
                .Select(g => g.Parent)
                .Where(p => !string.Equals(p.Code, UnreachableCode, StringComparison.Ordinal))
                .ToList();

        private static IReadOnlyList<DetectedIssue> ChildrenOf(
            DetectedIssue parent, Dictionary<string, List<DetectedIssue>> childrenByHost)
        {
            if (!IsUnreachableWithHost(parent)) return Array.Empty<DetectedIssue>();
            return childrenByHost.TryGetValue(NormalizeHost(parent.RelatedHost), out var list)
                ? list
                : (IReadOnlyList<DetectedIssue>)Array.Empty<DetectedIssue>();
        }

        private static bool IsUnreachableWithHost(DetectedIssue issue)
            => issue != null
               && string.Equals(issue.Code, UnreachableCode, StringComparison.Ordinal)
               && NormalizeHost(issue.RelatedHost) != null;

        private static bool IsDerivedFromUnreachableDc(DetectedIssue issue, HashSet<string> unreachableHosts)
        {
            if (IsUnreachableWithHost(issue)) return false;
            var host = NormalizeHost(issue.RelatedHost);
            return host != null && unreachableHosts.Contains(host);
        }

        /// <summary>Même normalisation que la détection croisée DNS/Ports : point final et casse ignorés.</summary>
        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return null;
            var normalized = host.Trim().TrimEnd('.');
            return normalized.Length == 0 ? null : normalized;
        }
    }
}
