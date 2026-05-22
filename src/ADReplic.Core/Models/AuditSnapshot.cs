using System;
using System.Collections.Generic;
using ADReplic.Core.Diagnostics.Issues;

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

        public IReadOnlyList<DetectedIssue> Issues { get; set; }

        /// <summary>Résultat de la sonde de santé DNS (SRV + A records). Null si la
        /// sonde n'a pas été exécutée pour cet audit ; dans ce cas le scoring
        /// renormalise les pondérations sur les sous-scores disponibles.</summary>
        public DnsHealthResult DnsHealth { get; set; }

        /// <summary>Résultat de la sonde de santé réseau (tests de ports TCP par DC).
        /// Null si la sonde n'a pas été exécutée ; même logique de renormalisation
        /// que DnsHealth côté HealthScoreCalculator.</summary>
        public PortHealthResult PortHealth { get; set; }

        /// <summary>
        /// Vrai si l'audit a porté sur un seul DC (mode ciblé) plutôt que sur
        /// toute la forêt. Permet aux détecteurs dont la prémisse repose sur
        /// un inventaire complet (ex : DC isolé, domaine mono-DC) de se taire
        /// pour éviter des faux positifs garantis par construction.
        /// </summary>
        public bool IsSingleDcMode { get; set; }

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
