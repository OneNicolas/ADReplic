using System;
using System.Collections.Generic;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Export
{
    /// <summary>
    /// Assemble un AuditSnapshot prêt à exporter à partir des résultats de collecte.
    /// Calcule également le résumé statistique en une seule passe.
    /// </summary>
    public static class AuditSnapshotBuilder
    {
        public static AuditSnapshot Build(
            string forestName,
            IReadOnlyList<DomainControllerInfo> domainControllers,
            IReadOnlyList<ReplicationLink> replicationLinks,
            TopologySnapshot topology = null,
            IReadOnlyList<ReplicationFailureInfo> failures = null,
            bool isSingleDcMode = false)
        {
            var snapshot = new AuditSnapshot
            {
                ForestName = forestName,
                GeneratedAt = DateTime.Now,
                GeneratedBy = Environment.UserName,
                GeneratedOn = Environment.MachineName,
                DomainControllers = domainControllers ?? Array.Empty<DomainControllerInfo>(),
                ReplicationLinks = replicationLinks ?? Array.Empty<ReplicationLink>(),
                ReplicationFailures = failures ?? Array.Empty<ReplicationFailureInfo>(),
                Sites = topology?.Sites ?? Array.Empty<SiteInfo>(),
                SiteLinks = topology?.SiteLinks ?? Array.Empty<SiteLinkInfo>(),
                IsSingleDcMode = isSingleDcMode,
                Summary = ComputeSummary(domainControllers, replicationLinks)
            };

            // Issues calculées avant le score : laisse la porte ouverte à une
            // future pondération du HealthScore par les issues détectées.
            snapshot.Issues = IssueAggregator.Aggregate(snapshot);

            // Score calculé en dernier : il s'appuie sur les champs déjà remplis.
            snapshot.HealthScore = HealthScoreCalculator.Compute(snapshot);

            return snapshot;
        }

        private static AuditSummary ComputeSummary(
            IReadOnlyList<DomainControllerInfo> dcs,
            IReadOnlyList<ReplicationLink> links)
        {
            var summary = new AuditSummary
            {
                DomainControllerCount = dcs?.Count ?? 0,
                ReplicationLinkCount = links?.Count ?? 0
            };

            if (links == null) return summary;

            foreach (var link in links)
            {
                switch (link.Status)
                {
                    case ReplicationLinkStatus.Healthy:
                        summary.HealthyLinks++;
                        break;
                    case ReplicationLinkStatus.Warning:
                        summary.WarningLinks++;
                        break;
                    case ReplicationLinkStatus.Failing:
                        summary.FailingLinks++;
                        break;
                    case ReplicationLinkStatus.Unreachable:
                        summary.UnreachableDcs++;
                        break;
                }
            }

            return summary;
        }
    }
}
