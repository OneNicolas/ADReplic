using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class SingleDcDomainDetectorTests
    {
        private static DomainControllerInfo Dc(string name, string domain) =>
            new DomainControllerInfo { HostName = name, Domain = domain };

        private static AuditSnapshot Snapshot(params DomainControllerInfo[] dcs) =>
            new AuditSnapshot { DomainControllers = dcs };

        [Fact]
        public void Returns_nothing_on_null_snapshot()
        {
            Assert.Empty(new SingleDcDomainDetector().Detect(null).ToList());
        }

        [Fact]
        public void Returns_nothing_when_each_domain_has_at_least_two_dcs()
        {
            var snap = Snapshot(
                Dc("DC01", "exemple.local"),
                Dc("DC02", "exemple.local"));

            Assert.Empty(new SingleDcDomainDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Flags_a_mono_dc_domain()
        {
            var snap = Snapshot(Dc("DC01", "exemple.local"));

            var issue = new SingleDcDomainDetector().Detect(snap).Single();
            Assert.Equal("DOMAIN_SINGLE_DC",     issue.Code);
            Assert.Equal(IssueSeverity.Warning,  issue.Severity);
            Assert.Contains("DC01",              issue.AffectedItems);
            Assert.Contains("exemple.local",     issue.Title);
        }

        [Fact]
        public void Flags_each_mono_dc_domain_in_a_multi_domain_forest()
        {
            // Forêt avec 2 domaines : un sain (2 DC), un en SPOF (1 DC).
            var snap = Snapshot(
                Dc("DC01", "exemple.local"),
                Dc("DC02", "exemple.local"),
                Dc("DC03", "filiale.exemple.local"));

            var issues = new SingleDcDomainDetector().Detect(snap).ToList();

            Assert.Single(issues);
            Assert.Contains("filiale.exemple.local", issues[0].Title);
        }

        [Fact]
        public void Grouping_is_case_insensitive_on_domain_name()
        {
            // Même domaine, casse différente : doit être considéré comme un seul groupe à 2 DC.
            var snap = Snapshot(
                Dc("DC01", "exemple.local"),
                Dc("DC02", "EXEMPLE.LOCAL"));

            Assert.Empty(new SingleDcDomainDetector().Detect(snap).ToList());
        }
    }
}
