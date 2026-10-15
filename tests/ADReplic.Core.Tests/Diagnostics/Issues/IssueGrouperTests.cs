using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// Agrégation de présentation : les anomalies conséquentes d'un DC injoignable
    /// (ports, SRV orphelins) sont rattachées à leur DC_UNREACHABLE pour ne pas
    /// noyer l'essentiel. La liste plate snapshot.Issues n'est jamais modifiée.
    /// </summary>
    public class IssueGrouperTests
    {
        private static DetectedIssue Issue(string code, IssueSeverity sev, string relatedHost = null, string title = null)
            => new DetectedIssue { Code = code, Severity = sev, Title = title ?? code, RelatedHost = relatedHost };

        private static DetectedIssue Unreachable(string host)
            => Issue("DC_UNREACHABLE", IssueSeverity.Critical, host, "Contrôleur de domaine injoignable : " + host);

        private static DetectedIssue CriticalPort(string host, int port)
            => Issue("PORT_CRITICAL_CLOSED", IssueSeverity.Critical, host, $"{host}:{port}");

        private static DetectedIssue SecondaryPort(string host, int port)
            => Issue("PORT_CLOSED_OR_FILTERED", IssueSeverity.Warning, host, $"{host}:{port}");

        private static DetectedIssue OrphanSrv(string host)
            => Issue("DNS_SRV_POINTS_TO_DEAD_DC", IssueSeverity.Warning, host);

        // -- Cas limites --

        [Fact]
        public void Null_or_empty_list_yields_no_group()
        {
            Assert.Empty(IssueGrouper.Group(null));
            Assert.Empty(IssueGrouper.Group(new DetectedIssue[0]));
        }

        [Fact]
        public void Issues_without_related_host_each_form_their_own_group()
        {
            var issues = new[]
            {
                Issue("DC_ISOLATED", IssueSeverity.Critical),
                Issue("SITE_NO_SUBNET", IssueSeverity.Warning)
            };

            var groups = IssueGrouper.Group(issues);

            Assert.Equal(2, groups.Count);
            Assert.All(groups, g => Assert.Empty(g.Children));
            Assert.All(groups, g => Assert.False(g.HasChildren));
            Assert.All(groups, g => Assert.Null(g.DerivedSummary));
        }

        [Fact]
        public void Unreachable_dc_without_consequences_stays_a_lone_parent()
        {
            var groups = IssueGrouper.Group(new[] { Unreachable("dc02.exemple.local") });

            var group = Assert.Single(groups);
            Assert.Equal("DC_UNREACHABLE", group.Parent.Code);
            Assert.Empty(group.Children);
            Assert.Null(group.DerivedSummary);
        }

        // -- Cas nominal : un DC éteint --

        [Fact]
        public void Consequences_of_an_unreachable_dc_become_its_children()
        {
            // Ordre d'entrée = ordre de l'agrégateur (sévérité décroissante puis code).
            var issues = new[]
            {
                Unreachable("dc02.exemple.local"),
                CriticalPort("dc02.exemple.local", 88),
                CriticalPort("dc02.exemple.local", 389),
                CriticalPort("dc02.exemple.local", 445),
                OrphanSrv("dc02.exemple.local"),
                SecondaryPort("dc02.exemple.local", 53),
                SecondaryPort("dc02.exemple.local", 636)
            };

            var groups = IssueGrouper.Group(issues);

            var group = Assert.Single(groups);
            Assert.Equal("DC_UNREACHABLE", group.Parent.Code);
            Assert.True(group.HasChildren);
            Assert.Equal(6, group.DerivedCount);
            Assert.Equal("6 anomalies liées à l'indisponibilité de dc02.exemple.local", group.DerivedSummary);
        }

        [Fact]
        public void Children_keep_the_input_order()
        {
            var issues = new[]
            {
                Unreachable("dc02.exemple.local"),
                CriticalPort("dc02.exemple.local", 88),
                OrphanSrv("dc02.exemple.local"),
                SecondaryPort("dc02.exemple.local", 53)
            };

            var children = IssueGrouper.Group(issues).Single().Children;

            Assert.Equal(
                new[] { "PORT_CRITICAL_CLOSED", "DNS_SRV_POINTS_TO_DEAD_DC", "PORT_CLOSED_OR_FILTERED" },
                children.Select(c => c.Code));
        }

        [Fact]
        public void Single_consequence_uses_singular_wording()
        {
            var issues = new[] { Unreachable("dc02.exemple.local"), CriticalPort("dc02.exemple.local", 389) };

            var group = IssueGrouper.Group(issues).Single();

            Assert.Equal("1 anomalie liée à l'indisponibilité de dc02.exemple.local", group.DerivedSummary);
        }

        // -- Garde-fous : on ne regroupe QUE derrière un DC_UNREACHABLE --

        [Fact]
        public void Port_issue_on_a_reachable_dc_stays_top_level()
        {
            // Un port fermé par pare-feu sur un DC qui répond n'a aucun parent : il doit rester visible.
            var issues = new[]
            {
                Unreachable("dc02.exemple.local"),
                CriticalPort("dc02.exemple.local", 389),
                CriticalPort("dc01.exemple.local", 445)
            };

            var groups = IssueGrouper.Group(issues);

            Assert.Equal(2, groups.Count);
            Assert.Equal("DC_UNREACHABLE", groups[0].Parent.Code);
            Assert.Single(groups[0].Children);
            Assert.Equal("PORT_CRITICAL_CLOSED", groups[1].Parent.Code);
            Assert.Equal("dc01.exemple.local", groups[1].Parent.RelatedHost);
            Assert.Empty(groups[1].Children);
        }

        [Fact]
        public void Two_unreachable_dcs_get_separate_groups()
        {
            var issues = new[]
            {
                Unreachable("dc02.exemple.local"),
                Unreachable("dc03.exemple.local"),
                CriticalPort("dc02.exemple.local", 389),
                CriticalPort("dc03.exemple.local", 389),
                CriticalPort("dc03.exemple.local", 88)
            };

            var groups = IssueGrouper.Group(issues);

            Assert.Equal(2, groups.Count);
            Assert.Equal("dc02.exemple.local", groups[0].Parent.RelatedHost);
            Assert.Single(groups[0].Children);
            Assert.Equal("dc03.exemple.local", groups[1].Parent.RelatedHost);
            Assert.Equal(2, groups[1].Children.Count);
        }

        [Fact]
        public void Host_matching_ignores_case_and_trailing_dot()
        {
            // Les cibles SRV issues du DNS portent souvent un point final et une casse différente.
            var issues = new[]
            {
                Unreachable("DC02.exemple.local"),
                OrphanSrv("dc02.exemple.local."),
                CriticalPort("Dc02.Exemple.Local", 389)
            };

            var group = Assert.Single(IssueGrouper.Group(issues));
            Assert.Equal(2, group.DerivedCount);
        }

        [Fact]
        public void Parents_keep_the_input_order_and_top_level_count_excludes_children()
        {
            var issues = new[]
            {
                Issue("DC_ISOLATED", IssueSeverity.Critical),
                Unreachable("dc02.exemple.local"),
                CriticalPort("dc02.exemple.local", 389),
                Issue("SITE_NO_SUBNET", IssueSeverity.Warning)
            };

            var groups = IssueGrouper.Group(issues);

            Assert.Equal(
                new[] { "DC_ISOLATED", "DC_UNREACHABLE", "SITE_NO_SUBNET" },
                groups.Select(g => g.Parent.Code));
        }
    }
}
