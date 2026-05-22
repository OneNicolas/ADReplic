using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Ad;
using ADReplic.Core.Models;

namespace ADReplic.Core.Topology
{
    /// <summary>
    /// Découvre sites, subnets, têtes de pont et liens inter-sites via System.DirectoryServices.ActiveDirectory.
    /// </summary>
    public sealed class TopologyProvider : ITopologyProvider
    {
        public Task<TopologySnapshot> GetAsync(AuditContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Task.Run(() => Collect(context, cancellationToken), cancellationToken);
        }

        private static TopologySnapshot Collect(AuditContext context, CancellationToken cancellationToken)
        {
            var forest = DirectoryContextFactory.OpenForest(context);

            var sites = new List<SiteInfo>();
            var linksByName = new Dictionary<string, SiteLinkInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (ActiveDirectorySite site in forest.Sites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sites.Add(BuildSiteInfo(site));
                CollectSiteLinks(site, linksByName);
            }

            return new TopologySnapshot
            {
                Sites = sites,
                SiteLinks = linksByName.Values
                    .OrderBy(l => l.Cost)
                    .ThenBy(l => l.Name)
                    .ToList()
            };
        }

        private static SiteInfo BuildSiteInfo(ActiveDirectorySite site)
        {
            return new SiteInfo
            {
                Name = site.Name,
                Location = site.Location,
                DomainControllers = SafeNames(site.Servers),
                Subnets = SafeSubnetNames(site.Subnets),
                BridgeheadServers = SafeNames(site.BridgeheadServers)
            };
        }

        private static void CollectSiteLinks(ActiveDirectorySite site, IDictionary<string, SiteLinkInfo> sink)
        {
            // Un même site link est référencé par chacun des sites qu'il relie ; on dédoublonne par nom.
            foreach (ActiveDirectorySiteLink link in site.SiteLinks)
            {
                if (sink.ContainsKey(link.Name)) continue;
                sink[link.Name] = BuildLinkInfo(link);
            }
        }

        private static SiteLinkInfo BuildLinkInfo(ActiveDirectorySiteLink link)
        {
            return new SiteLinkInfo
            {
                Name = link.Name,
                Cost = link.Cost,
                ReplicationInterval = link.ReplicationInterval,
                TransportType = link.TransportType.ToString(),
                ChangeNotificationEnabled = link.NotificationEnabled,
                Sites = link.Sites
                    .Cast<ActiveDirectorySite>()
                    .Select(s => s.Name)
                    .ToList()
            };
        }

        private static IReadOnlyList<string> SafeNames<T>(System.Collections.IEnumerable collection) where T : class
        {
            if (collection == null) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var item in collection)
            {
                // ActiveDirectorySite expose Server (DomainController), BridgeheadServers expose aussi DC : tous ont une propriété Name.
                var nameProp = item?.GetType().GetProperty("Name");
                if (nameProp != null)
                {
                    var value = nameProp.GetValue(item, null) as string;
                    if (!string.IsNullOrEmpty(value)) list.Add(value);
                }
            }
            return list;
        }

        private static IReadOnlyList<string> SafeNames(System.Collections.IEnumerable collection)
            => SafeNames<object>(collection);

        private static IReadOnlyList<string> SafeSubnetNames(System.Collections.IEnumerable subnets)
        {
            if (subnets == null) return Array.Empty<string>();
            var list = new List<string>();
            foreach (ActiveDirectorySubnet s in subnets)
            {
                if (s != null && !string.IsNullOrEmpty(s.Name)) list.Add(s.Name);
            }
            return list;
        }
    }
}
