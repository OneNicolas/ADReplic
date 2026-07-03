using System;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Différentiel complet entre deux <see cref="AuditSnapshot"/>. Pivot consommé
    /// par la GUI (onglet Comparaison) et par <see cref="SignificantChangeDetector"/>
    /// pour décider d'une alerte en mode auto-refresh.
    /// </summary>
    /// <remarks>
    /// Ne porte aucune logique : ce n'est qu'un transport. L'absence de méthode
    /// permet de le sérialiser proprement et de le tester sans effets de bord.
    /// </remarks>
    public sealed class AuditDiff
    {
        public DateTime ReferenceGeneratedAt { get; set; }
        public DateTime CurrentGeneratedAt { get; set; }

        public string ReferenceForestName { get; set; }
        public string CurrentForestName { get; set; }

        /// <summary>
        /// Vrai si les noms de forêt diffèrent entre les deux audits. Permet à
        /// la GUI d'afficher un bandeau d'avertissement (comparer deux forêts
        /// distinctes a peu de sens fonctionnellement).
        /// </summary>
        public bool DifferentForests =>
            !string.Equals(
                ReferenceForestName ?? string.Empty,
                CurrentForestName ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

        public SnapshotDelta<DomainControllerInfo> DomainControllers { get; set; } = new SnapshotDelta<DomainControllerInfo>();
        public SnapshotDelta<ReplicationLink> ReplicationLinks { get; set; } = new SnapshotDelta<ReplicationLink>();
        public SnapshotDelta<ReplicationFailureInfo> ReplicationFailures { get; set; } = new SnapshotDelta<ReplicationFailureInfo>();
        public SnapshotDelta<SiteInfo> Sites { get; set; } = new SnapshotDelta<SiteInfo>();
        public SnapshotDelta<SiteLinkInfo> SiteLinks { get; set; } = new SnapshotDelta<SiteLinkInfo>();
        public SnapshotDelta<DetectedIssue> Issues { get; set; } = new SnapshotDelta<DetectedIssue>();
        public SnapshotDelta<DnsCheckResult> DnsChecks { get; set; } = new SnapshotDelta<DnsCheckResult>();
        public SnapshotDelta<PortCheckResult> PortChecks { get; set; } = new SnapshotDelta<PortCheckResult>();

        /// <summary>Évolution du score. Null si l'une des deux snapshots n'avait pas de score calculé.</summary>
        public HealthScoreDelta Score { get; set; }

        /// <summary>Vrai si aucune différence n'a été détectée dans aucune catégorie.</summary>
        public bool IsEmpty =>
            DomainControllers.IsEmpty
            && ReplicationLinks.IsEmpty
            && ReplicationFailures.IsEmpty
            && Sites.IsEmpty
            && SiteLinks.IsEmpty
            && Issues.IsEmpty
            && DnsChecks.IsEmpty
            && PortChecks.IsEmpty
            && (Score == null || (Score.Delta == 0 && !Score.LevelChanged));
    }
}
