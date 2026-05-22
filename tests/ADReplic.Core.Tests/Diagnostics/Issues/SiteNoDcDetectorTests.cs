using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class SiteNoDcDetectorTests
    {
        private static SiteInfo Site(string name, string[] dcs = null) =>
            new SiteInfo
            {
                Name              = name,
                DomainControllers = dcs ?? new string[0]
            };

        private static AuditSnapshot Snapshot(params SiteInfo[] sites) =>
            new AuditSnapshot { Sites = sites };

        [Fact]
        public void Returns_nothing_on_null_snapshot()
        {
            Assert.Empty(new SiteNoDcDetector().Detect(null).ToList());
        }

        [Fact]
        public void Returns_nothing_when_all_sites_have_dcs()
        {
            var snap = Snapshot(
                Site("Paris", new[] { "DC01" }),
                Site("Lyon",  new[] { "DC02" }));

            Assert.Empty(new SiteNoDcDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Flags_an_empty_site_as_info()
        {
            var snap = Snapshot(
                Site("Paris", new[] { "DC01" }),
                Site("Reserve"));

            var issue = new SiteNoDcDetector().Detect(snap).Single();
            Assert.Equal("SITE_NO_DC",         issue.Code);
            Assert.Equal(IssueSeverity.Info,   issue.Severity);
            Assert.Contains("Reserve",         issue.AffectedItems);
        }
    }
}
