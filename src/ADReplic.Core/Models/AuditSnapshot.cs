using System;
using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Snapshot complet d'un audit, exportable tel quel.
    /// Sert de transport entre la couche de collecte et les exporters.
    /// </summary>
    public sealed class AuditSnapshot
    {
        public string ForestName { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; }
        public string GeneratedOn { get; set; }

        public IReadOnlyList<DomainControllerInfo> DomainControllers { get; set; }
        public IReadOnlyList<ReplicationLink> ReplicationLinks { get; set; }
        public IReadOnlyList<ReplicationFailureInfo> ReplicationFailures { get; set; }
        public IReadOnlyList<SiteInfo> Sites { get; set; }
        public IReadOnlyList<SiteLinkInfo> SiteLinks { get; set; }

        public AuditSummary Summary { get; set; }
        public HealthScore HealthScore { get; set; }
    }

    public sealed class AuditSummary
    {
        public int DomainControllerCount { get; set; }
        public int ReplicationLinkCount { get; set; }
        public int HealthyLinks { get; set; }
        public int WarningLinks { get; set; }
        public int FailingLinks { get; set; }
        public int UnreachableDcs { get; set; }
    }
}
