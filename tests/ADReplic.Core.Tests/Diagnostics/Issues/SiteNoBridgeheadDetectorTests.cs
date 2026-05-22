using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class SiteNoBridgeheadDetectorTests
    {
        private static SiteInfo Site(string name, string[] dcs = null, string[] bridgeheads = null) =>
            new SiteInfo
            {
                Name              = name,
                DomainControllers = dcs ?? new string[0],
                BridgeheadServers = bridgeheads ?? new string[0]
            };

        private static AuditSnapshot Snapshot(params SiteInfo[] sites) =>
            new AuditSnapshot { Sites = sites };

        [Fact]
        public void Returns_nothing_on_null_snapshot()
        {
            Assert.Empty(new SiteNoBridgeheadDetector().Detect(null).ToList());
        }

        [Fact]
        public void Returns_nothing_on_single_site_forest()
        {
            // Forêt mono-site : le bridgehead inter-sites n'a pas de sens.
            var snap = Snapshot(Site("Default-First-Site-Name", new[] { "DC01" }));
            Assert.Empty(new SiteNoBridgeheadDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Returns_nothing_when_site_has_no_dcs()
        {
            // Un site vide est traité par SiteNoDcDetector, pas ici.
            var snap = Snapshot(
                Site("Paris",  new[] { "DC01" }, new[] { "DC01" }),
                Site("Lyon"));

            Assert.Empty(new SiteNoBridgeheadDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Returns_nothing_when_all_sites_have_bridgeheads()
        {
            var snap = Snapshot(
                Site("Paris", new[] { "DC01" }, new[] { "DC01" }),
                Site("Lyon",  new[] { "DC02" }, new[] { "DC02" }));

            Assert.Empty(new SiteNoBridgeheadDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Flags_a_site_with_dcs_but_no_bridgehead()
        {
            var snap = Snapshot(
                Site("Paris", new[] { "DC01" }, new[] { "DC01" }),
                Site("Lyon",  new[] { "DC02" }));

            var issue = new SiteNoBridgeheadDetector().Detect(snap).Single();
            Assert.Equal("SITE_NO_BRIDGEHEAD",   issue.Code);
            Assert.Equal(IssueSeverity.Warning,  issue.Severity);
            Assert.Contains("Lyon",              issue.AffectedItems);
        }
    }
}
