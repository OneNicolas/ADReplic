using System;
using System.Linq;
using ADReplic.Core.Models;
using ADReplic.Core.Topology;
using Xunit;

namespace ADReplic.Core.Tests.Topology
{
    /// <summary>
    /// Vue topologie (v0.9.0) : le layout est pur et testable, la GUI ne fait que dessiner
    /// ce qu'il renvoie (positions, arêtes, tiers de coût) dans un canevas fixe.
    /// </summary>
    public class TopologyLayoutTests
    {
        private static SiteInfo Site(string name, params string[] dcs) =>
            new SiteInfo { Name = name, DomainControllers = dcs };

        private static SiteLinkInfo Link(string name, int cost, params string[] sites) =>
            new SiteLinkInfo { Name = name, Cost = cost, ReplicationInterval = TimeSpan.FromMinutes(180), TransportType = "IP", Sites = sites };

        private static DomainControllerInfo Dc(string host, string site, bool reachable = true) =>
            new DomainControllerInfo { HostName = host, SiteName = site, IsReachable = reachable };

        private static bool InsideCanvas(TopologyNode n) =>
            n.X - n.Width / 2 >= 0 && n.X + n.Width / 2 <= TopologyLayout.CanvasWidth
            && n.Y - n.Height / 2 >= 0 && n.Y + n.Height / 2 <= TopologyLayout.CanvasHeight;

        [Fact]
        public void Empty_topology_yields_empty_layout()
        {
            var layout = TopologyLayout.Build(null, null, null);

            Assert.Empty(layout.Nodes);
            Assert.Empty(layout.Edges);
        }

        [Fact]
        public void Single_site_is_centered_without_edges()
        {
            var layout = TopologyLayout.Build(new[] { Site("Paris", "dc01", "dc02") }, new SiteLinkInfo[0], new[] { Dc("dc01", "Paris"), Dc("dc02", "Paris") });

            var node = Assert.Single(layout.Nodes);
            Assert.Equal(TopologyLayout.CanvasWidth / 2, node.X);
            Assert.Equal(TopologyLayout.CanvasHeight / 2, node.Y);
            Assert.Equal(2, node.DomainControllers.Count);
            Assert.Empty(layout.Edges);
        }

        [Fact]
        public void Up_to_eight_sites_are_laid_out_on_a_circle_inside_the_canvas()
        {
            var sites = Enumerable.Range(1, 8).Select(i => Site("Site" + i, "dc" + i)).ToArray();

            var layout = TopologyLayout.Build(sites, new SiteLinkInfo[0], new DomainControllerInfo[0]);

            Assert.Equal(8, layout.Nodes.Count);
            Assert.All(layout.Nodes, n => Assert.True(InsideCanvas(n), n.SiteName + " sort du canevas"));
            Assert.Equal(8, layout.Nodes.Select(n => (Math.Round(n.X), Math.Round(n.Y))).Distinct().Count());
            // Même distance au centre pour tous : c'est un cercle.
            var radii = layout.Nodes.Select(n => Math.Round(Math.Sqrt(Math.Pow(n.X - TopologyLayout.CanvasWidth / 2, 2) + Math.Pow(n.Y - TopologyLayout.CanvasHeight / 2, 2)))).Distinct();
            Assert.Single(radii);
        }

        [Fact]
        public void More_than_eight_sites_fall_back_to_a_grid_inside_the_canvas()
        {
            var sites = Enumerable.Range(1, 12).Select(i => Site("Site" + i, "dc" + i)).ToArray();

            var layout = TopologyLayout.Build(sites, new SiteLinkInfo[0], new DomainControllerInfo[0]);

            Assert.Equal(12, layout.Nodes.Count);
            Assert.All(layout.Nodes, n => Assert.True(InsideCanvas(n), n.SiteName + " sort du canevas"));
            Assert.Equal(12, layout.Nodes.Select(n => (Math.Round(n.X), Math.Round(n.Y))).Distinct().Count());
        }

        [Fact]
        public void Two_site_link_becomes_one_edge_between_node_centers()
        {
            var layout = TopologyLayout.Build(
                new[] { Site("Paris", "dc01"), Site("Lyon", "dc02") },
                new[] { Link("Paris-Lyon", 100, "Paris", "Lyon") },
                new DomainControllerInfo[0]);

            var edge = Assert.Single(layout.Edges);
            var paris = layout.Nodes.Single(n => n.SiteName == "Paris");
            var lyon = layout.Nodes.Single(n => n.SiteName == "Lyon");
            Assert.Equal("Paris-Lyon", edge.LinkName);
            Assert.Equal(100, edge.Cost);
            Assert.Equal((paris.X, paris.Y), (edge.X1, edge.Y1));
            Assert.Equal((lyon.X, lyon.Y), (edge.X2, edge.Y2));
        }

        [Fact]
        public void Multi_site_link_becomes_a_clique_of_edges()
        {
            var layout = TopologyLayout.Build(
                new[] { Site("A"), Site("B"), Site("C") },
                new[] { Link("ABC", 50, "A", "B", "C") },
                new DomainControllerInfo[0]);

            Assert.Equal(3, layout.Edges.Count);
            Assert.All(layout.Edges, e => Assert.Equal("ABC", e.LinkName));
        }

        [Fact]
        public void Cost_tiers_are_relative_to_the_forest_min_and_max()
        {
            var layout = TopologyLayout.Build(
                new[] { Site("A"), Site("B"), Site("C"), Site("D") },
                new[] { Link("cheap", 100, "A", "B"), Link("mid", 200, "B", "C"), Link("dear", 400, "C", "D") },
                new DomainControllerInfo[0]);

            Assert.Equal(CostTier.Low, layout.Edges.Single(e => e.LinkName == "cheap").CostTier);
            Assert.Equal(CostTier.Medium, layout.Edges.Single(e => e.LinkName == "mid").CostTier);
            Assert.Equal(CostTier.High, layout.Edges.Single(e => e.LinkName == "dear").CostTier);
        }

        [Fact]
        public void Uniform_costs_are_all_low_tier()
        {
            var layout = TopologyLayout.Build(
                new[] { Site("A"), Site("B"), Site("C") },
                new[] { Link("l1", 100, "A", "B"), Link("l2", 100, "B", "C") },
                new DomainControllerInfo[0]);

            Assert.All(layout.Edges, e => Assert.Equal(CostTier.Low, e.CostTier));
        }

        [Fact]
        public void Node_carries_dc_reachability_from_the_inventory()
        {
            var layout = TopologyLayout.Build(
                new[] { Site("Paris", "dc01.exemple.local", "dc02.exemple.local") },
                new SiteLinkInfo[0],
                new[] { Dc("DC01.exemple.local", "Paris"), Dc("dc02.exemple.local", "Paris", reachable: false) });

            var node = Assert.Single(layout.Nodes);
            Assert.Equal(1, node.UnreachableCount);
            Assert.False(node.DomainControllers.Single(d => d.HostName == "dc02.exemple.local").IsReachable);
            Assert.True(node.DomainControllers.Single(d => d.HostName == "dc01.exemple.local").IsReachable);
        }

        [Fact]
        public void Dc_known_only_from_inventory_is_attached_to_its_site()
        {
            // Le site est vide côté topologie (lecture partielle) mais l'inventaire connaît un DC dedans.
            var layout = TopologyLayout.Build(new[] { Site("Paris") }, new SiteLinkInfo[0], new[] { Dc("dc01", "Paris") });

            Assert.Single(Assert.Single(layout.Nodes).DomainControllers);
        }

        [Fact]
        public void Link_to_an_unknown_site_creates_a_placeholder_node()
        {
            var layout = TopologyLayout.Build(
                new[] { Site("Paris", "dc01") },
                new[] { Link("Paris-Nantes", 100, "Paris", "Nantes") },
                new DomainControllerInfo[0]);

            Assert.Equal(2, layout.Nodes.Count);
            Assert.Empty(layout.Nodes.Single(n => n.SiteName == "Nantes").DomainControllers);
            Assert.Single(layout.Edges);
        }

        [Fact]
        public void Node_height_grows_with_dc_count_but_stays_bounded()
        {
            var small = TopologyLayout.Build(new[] { Site("A", "dc1") }, new SiteLinkInfo[0], new DomainControllerInfo[0]).Nodes.Single();
            var big = TopologyLayout.Build(new[] { Site("A", Enumerable.Range(1, 30).Select(i => "dc" + i).ToArray()) }, new SiteLinkInfo[0], new DomainControllerInfo[0]).Nodes.Single();

            Assert.True(big.Height > small.Height);
            Assert.True(big.Height <= TopologyLayout.MaxNodeHeight);
            Assert.True(big.HiddenDcCount > 0);
        }

        [Fact]
        public void Node_height_leaves_room_for_a_two_line_header_and_each_dc_row()
        {
            // Retour terrain : avec 44 px d'en-tête, la carte coupait le lieu et le premier DC.
            // Padding 12 + nom 15 px (~22) + lieu 11 px (~18) + marge 4 = 56 au minimum.
            var node = TopologyLayout.Build(new[] { new SiteInfo { Name = "Paris", Location = "Siège", DomainControllers = new[] { "dc1", "dc2" } } },
                new SiteLinkInfo[0], new DomainControllerInfo[0]).Nodes.Single();

            Assert.True(TopologyLayout.NodeHeaderHeight >= 56);
            Assert.Equal(TopologyLayout.NodeHeaderHeight + 2 * TopologyLayout.DcRowHeight, node.Height);
        }
    }
}
