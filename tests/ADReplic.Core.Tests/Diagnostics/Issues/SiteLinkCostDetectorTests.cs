using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class SiteLinkCostDetectorTests
    {
        private static SiteLinkInfo Link(string name, int cost) =>
            new SiteLinkInfo { Name = name, Cost = cost };

        private static AuditSnapshot Snapshot(params SiteLinkInfo[] links) =>
            new AuditSnapshot { SiteLinks = links };

        [Fact]
        public void Returns_nothing_on_null_snapshot()
        {
            Assert.Empty(new SiteLinkCostDetector().Detect(null).ToList());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(10000)]
        public void Cost_in_sane_range_is_not_flagged(int cost)
        {
            var snap = Snapshot(Link("DefaultIPSiteLink", cost));
            Assert.Empty(new SiteLinkCostDetector().Detect(snap).ToList());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-50)]
        [InlineData(10001)]
        [InlineData(99999)]
        public void Cost_out_of_range_is_flagged(int cost)
        {
            var snap = Snapshot(Link("DefaultIPSiteLink", cost));

            var issue = new SiteLinkCostDetector().Detect(snap).Single();
            Assert.Equal("SITE_LINK_COST_ABERRANT", issue.Code);
            Assert.Equal(IssueSeverity.Warning,    issue.Severity);
            Assert.Contains("DefaultIPSiteLink",   issue.AffectedItems);
        }

        [Fact]
        public void Returns_one_issue_per_aberrant_link()
        {
            var snap = Snapshot(
                Link("Link-OK",         100),
                Link("Link-Negative",   -1),
                Link("Link-TooHigh",    50000));

            var issues = new SiteLinkCostDetector().Detect(snap).ToList();

            Assert.Equal(2, issues.Count);
            Assert.Contains(issues, i => i.AffectedItems.Contains("Link-Negative"));
            Assert.Contains(issues, i => i.AffectedItems.Contains("Link-TooHigh"));
        }
    }
}
