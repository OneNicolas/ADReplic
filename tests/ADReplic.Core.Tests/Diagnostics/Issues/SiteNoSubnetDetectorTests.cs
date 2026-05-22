using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class SiteNoSubnetDetectorTests
    {
        private static SiteInfo Site(string name, string[] dcs = null, string[] subnets = null) =>
            new SiteInfo
            {
                Name              = name,
                DomainControllers = dcs ?? new string[0],
                Subnets           = subnets ?? new string[0]
            };

        private static AuditSnapshot Snapshot(params SiteInfo[] sites) =>
            new AuditSnapshot { Sites = sites };

        [Fact]
        public void Returns_nothing_on_null_snapshot()
        {
            Assert.Empty(new SiteNoSubnetDetector().Detect(null).ToList());
        }

        [Fact]
        public void Returns_nothing_when_all_sites_have_subnets()
        {
            var snap = Snapshot(
                Site("Paris", new[] { "DC01" }, new[] { "10.10.0.0/24" }),
                Site("Lyon",  new[] { "DC02" }, new[] { "10.20.0.0/24" }));

            Assert.Empty(new SiteNoSubnetDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Returns_nothing_when_site_has_no_dcs()
        {
            // Site vide : SiteNoDcDetector s'en charge.
            var snap = Snapshot(Site("Reserve"));
            Assert.Empty(new SiteNoSubnetDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Flags_a_site_with_dcs_but_no_subnet()
        {
            var snap = Snapshot(
                Site("Paris", new[] { "DC01" }, new[] { "10.10.0.0/24" }),
                Site("Lyon",  new[] { "DC02" }));

            var issue = new SiteNoSubnetDetector().Detect(snap).Single();
            Assert.Equal("SITE_NO_SUBNET",       issue.Code);
            Assert.Equal(IssueSeverity.Warning,  issue.Severity);
            Assert.Contains("Lyon",              issue.AffectedItems);
        }
    }
}
