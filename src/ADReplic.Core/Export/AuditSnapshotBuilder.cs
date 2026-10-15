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
            bool isSingleDcMode = false,
            DnsHealthResult dnsHealth = null,
            PortHealthResult portHealth = null)
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
                DnsHealth = dnsHealth,
                PortHealth = portHealth,
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

        /// <summary>
        /// Rejoue détecteurs, synthèse et score sur un audit relu depuis un fichier
        /// (v0.8.0, « Ouvrir un audit JSON »). Les données collectées sont conservées
        /// telles quelles, ainsi que la date, l'auteur et le poste d'origine : seules
        /// les conclusions sont recalculées avec la version courante — un audit
        /// archivé par une version antérieure profite ainsi des détecteurs et du
        /// regroupement d'aujourd'hui.
        /// </summary>
        public static AuditSnapshot Rebuild(AuditSnapshot source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var topology = new TopologySnapshot { Sites = source.Sites, SiteLinks = source.SiteLinks };
            var rebuilt = Build(
                source.ForestName,
                source.DomainControllers,
                source.ReplicationLinks,
                topology,
                source.ReplicationFailures,
                source.IsSingleDcMode,
                source.DnsHealth,
                source.PortHealth);

            rebuilt.GeneratedAt = source.GeneratedAt;
            rebuilt.GeneratedBy = source.GeneratedBy;
            rebuilt.GeneratedOn = source.GeneratedOn;
            return rebuilt;
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
