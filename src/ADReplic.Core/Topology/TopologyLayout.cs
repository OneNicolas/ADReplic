using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Topology
{
    public enum CostTier { Low, Medium, High }

    public sealed class TopologyDc
    {
        public string HostName { get; set; }
        public bool IsReachable { get; set; } = true;
    }

    /// <summary>Un site, positionné par son centre dans le canevas.</summary>
    public sealed class TopologyNode
    {
        public string SiteName { get; set; }
        public string Location { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public IReadOnlyList<TopologyDc> DomainControllers { get; set; } = new List<TopologyDc>();
        /// <summary>Les DC réellement affichés dans la carte ; le reste est résumé par HiddenDcCount.</summary>
        public IReadOnlyList<TopologyDc> VisibleDomainControllers { get; set; } = new List<TopologyDc>();
        public int HiddenDcCount { get; set; }
        public int UnreachableCount { get; set; }
        public bool HasUnreachableDc => UnreachableCount > 0;
        public bool HasHiddenDcs => HiddenDcCount > 0;
        public bool HasNoDc => DomainControllers.Count == 0;
        public double Left => X - Width / 2;
        public double Top => Y - Height / 2;
    }

    /// <summary>Un lien inter-sites entre deux sites (un lien à N sites donne N(N−1)/2 arêtes).</summary>
    public sealed class TopologyEdge
    {
        public string LinkName { get; set; }
        public string FromSite { get; set; }
        public string ToSite { get; set; }
        public int Cost { get; set; }
        public CostTier CostTier { get; set; }
        public TimeSpan ReplicationInterval { get; set; }
        public string TransportType { get; set; }
        public bool ChangeNotificationEnabled { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double LabelX => (X1 + X2) / 2;
        public double LabelY => (Y1 + Y2) / 2;
    }

    public sealed class TopologyGraph
    {
        public IReadOnlyList<TopologyNode> Nodes { get; set; } = new List<TopologyNode>();
        public IReadOnlyList<TopologyEdge> Edges { get; set; } = new List<TopologyEdge>();
        public bool IsEmpty => Nodes.Count == 0;
    }

    /// <summary>
    /// Vue topologie (v0.9.0) : calcul pur des positions dans un canevas fixe que la GUI
    /// met à l'échelle. Cercle jusqu'à 8 sites (lisible, toutes les arêtes visibles),
    /// grille au-delà. Les tiers de coût sont relatifs au min/max de la forêt : ce qui
    /// compte pour l'administrateur est « quel lien est le plus cher ici », pas la valeur brute.
    /// </summary>
    public static class TopologyLayout
    {
        public const double CanvasWidth = 1000;
        public const double CanvasHeight = 620;
        public const double NodeWidth = 220;
        // En-tête = padding vertical de la carte (12) + nom du site 15 px (~22) + lieu 11 px (~18) + marge (4) + réserve.
        public const double NodeHeaderHeight = 64;
        public const double DcRowHeight = 20;
        public const int MaxVisibleDcs = 5;
        public static readonly double MaxNodeHeight = NodeHeaderHeight + (MaxVisibleDcs + 1) * DcRowHeight;
        private const double Margin = 20;
        private const int MaxSitesOnCircle = 8;

        public static TopologyGraph Build(IEnumerable<SiteInfo> sites, IEnumerable<SiteLinkInfo> siteLinks, IEnumerable<DomainControllerInfo> dcs)
        {
            var siteList = (sites ?? Enumerable.Empty<SiteInfo>()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            var linkList = (siteLinks ?? Enumerable.Empty<SiteLinkInfo>()).Where(l => l != null && l.Sites != null).ToList();
            var dcList = (dcs ?? Enumerable.Empty<DomainControllerInfo>()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.HostName)).ToList();

            var nodes = BuildNodes(siteList, linkList, dcList);
            if (nodes.Count == 0) return new TopologyGraph();

            Place(nodes);
            var edges = BuildEdges(linkList, nodes);
            return new TopologyGraph { Nodes = nodes, Edges = edges };
        }

        private static List<TopologyNode> BuildNodes(List<SiteInfo> sites, List<SiteLinkInfo> links, List<DomainControllerInfo> dcs)
        {
            var reachability = new Dictionary<string, bool>(HostNameNormalizer.Comparer);
            foreach (var dc in dcs)
            {
                var key = HostNameNormalizer.Normalize(dc.HostName);
                if (key != null) reachability[key] = dc.IsReachable;
            }

            var nodes = new List<TopologyNode>();
            var byName = new Dictionary<string, TopologyNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var site in sites)
            {
                if (byName.ContainsKey(site.Name)) continue;
                var hosts = new List<string>(site.DomainControllers ?? Array.Empty<string>());
                // L'inventaire peut connaître un DC que la lecture de topologie n'a pas rattaché au site.
                foreach (var dc in dcs.Where(d => string.Equals(d.SiteName, site.Name, StringComparison.OrdinalIgnoreCase)))
                    if (!hosts.Any(h => HostNameNormalizer.AreSame(h, dc.HostName))) hosts.Add(dc.HostName);

                var node = NewNode(site.Name, site.Location, hosts, reachability);
                nodes.Add(node);
                byName[site.Name] = node;
            }

            // Un lien vers un site absent de la liste : nœud vide plutôt qu'arête perdue.
            foreach (var siteName in links.SelectMany(l => l.Sites).Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (byName.ContainsKey(siteName)) continue;
                var node = NewNode(siteName, null, new List<string>(), reachability);
                nodes.Add(node);
                byName[siteName] = node;
            }
            return nodes;
        }

        private static TopologyNode NewNode(string name, string location, List<string> hosts, Dictionary<string, bool> reachability)
        {
            var dcs = hosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => new TopologyDc
            {
                HostName = h,
                IsReachable = !reachability.TryGetValue(HostNameNormalizer.Normalize(h) ?? "", out var reachable) || reachable
            }).OrderBy(d => d.IsReachable ? 1 : 0).ThenBy(d => d.HostName, StringComparer.OrdinalIgnoreCase).ToList();

            var visible = dcs.Take(MaxVisibleDcs).ToList();
            var hidden = dcs.Count - visible.Count;
            return new TopologyNode
            {
                SiteName = name,
                Location = location,
                DomainControllers = dcs,
                VisibleDomainControllers = visible,
                HiddenDcCount = hidden,
                UnreachableCount = dcs.Count(d => !d.IsReachable),
                Width = NodeWidth,
                Height = NodeHeaderHeight + Math.Max(1, visible.Count + (hidden > 0 ? 1 : 0)) * DcRowHeight
            };
        }

        private static void Place(List<TopologyNode> nodes)
        {
            var cx = CanvasWidth / 2;
            var cy = CanvasHeight / 2;
            var maxHeight = nodes.Max(n => n.Height);

            if (nodes.Count == 1)
            {
                nodes[0].X = cx;
                nodes[0].Y = cy;
                return;
            }

            if (nodes.Count <= MaxSitesOnCircle)
            {
                var radius = Math.Min(cx - NodeWidth / 2 - Margin, cy - maxHeight / 2 - Margin);
                for (var i = 0; i < nodes.Count; i++)
                {
                    var angle = -Math.PI / 2 + 2 * Math.PI * i / nodes.Count;
                    nodes[i].X = cx + radius * Math.Cos(angle);
                    nodes[i].Y = cy + radius * Math.Sin(angle);
                }
                return;
            }

            var cols = (int)Math.Ceiling(Math.Sqrt(nodes.Count));
            var rows = (int)Math.Ceiling(nodes.Count / (double)cols);
            var cellW = CanvasWidth / cols;
            var cellH = CanvasHeight / rows;
            for (var i = 0; i < nodes.Count; i++)
            {
                nodes[i].X = cellW * (i % cols) + cellW / 2;
                nodes[i].Y = cellH * (i / cols) + cellH / 2;
            }
        }

        private static List<TopologyEdge> BuildEdges(List<SiteLinkInfo> links, List<TopologyNode> nodes)
        {
            var byName = nodes.ToDictionary(n => n.SiteName, n => n, StringComparer.OrdinalIgnoreCase);
            var costs = links.Select(l => l.Cost).ToList();
            var min = costs.Count > 0 ? costs.Min() : 0;
            var max = costs.Count > 0 ? costs.Max() : 0;

            var edges = new List<TopologyEdge>();
            foreach (var link in links)
            {
                var members = link.Sites.Where(s => !string.IsNullOrWhiteSpace(s) && byName.ContainsKey(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                for (var i = 0; i < members.Count; i++)
                for (var j = i + 1; j < members.Count; j++)
                {
                    var from = byName[members[i]];
                    var to = byName[members[j]];
                    edges.Add(new TopologyEdge
                    {
                        LinkName = link.Name,
                        FromSite = from.SiteName,
                        ToSite = to.SiteName,
                        Cost = link.Cost,
                        CostTier = Tier(link.Cost, min, max),
                        ReplicationInterval = link.ReplicationInterval,
                        TransportType = link.TransportType,
                        ChangeNotificationEnabled = link.ChangeNotificationEnabled,
                        X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y
                    });
                }
            }
            return edges;
        }

        /// <summary>Tiers relatifs au min/max de la forêt ; coûts uniformes = tous bas (rien à distinguer).</summary>
        private static CostTier Tier(int cost, int min, int max)
        {
            if (max <= min || cost <= min) return CostTier.Low;
            var span = (double)(max - min);
            if (cost < min + span / 3) return CostTier.Low;
            if (cost < min + 2 * span / 3) return CostTier.Medium;
            return CostTier.High;
        }
    }
}
