using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class IssueAggregatorTests
    {
        /// <summary>Détecteur de test qui retourne des issues prédéfinies.</summary>
        private sealed class StubDetector : IIssueDetector
        {
            private readonly DetectedIssue[] _issues;
            public StubDetector(params DetectedIssue[] issues) { _issues = issues; }
            public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot) => _issues;
        }

        private static DetectedIssue Issue(string code, IssueSeverity severity, string title = "x") =>
            new DetectedIssue { Code = code, Severity = severity, Title = title };

        [Fact]
        public void Returns_empty_when_snapshot_is_null()
        {
            Assert.Empty(IssueAggregator.Aggregate(null));
        }

        [Fact]
        public void Returns_empty_when_detectors_collection_is_null()
        {
            Assert.Empty(IssueAggregator.Aggregate(new AuditSnapshot(), null));
        }

        [Fact]
        public void Concatenates_issues_from_all_detectors()
        {
            var detectors = new IIssueDetector[]
            {
                new StubDetector(Issue("A", IssueSeverity.Info)),
                new StubDetector(Issue("B", IssueSeverity.Warning), Issue("C", IssueSeverity.Critical))
            };

            var result = IssueAggregator.Aggregate(new AuditSnapshot(), detectors);

            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void Orders_critical_before_warning_before_info()
        {
            var detectors = new IIssueDetector[]
            {
                new StubDetector(
                    Issue("A", IssueSeverity.Info),
                    Issue("B", IssueSeverity.Critical),
                    Issue("C", IssueSeverity.Warning))
            };

            var result = IssueAggregator.Aggregate(new AuditSnapshot(), detectors);

            Assert.Equal(IssueSeverity.Critical, result[0].Severity);
            Assert.Equal(IssueSeverity.Warning,  result[1].Severity);
            Assert.Equal(IssueSeverity.Info,     result[2].Severity);
        }

        [Fact]
        public void Ties_on_severity_are_broken_by_code_then_title()
        {
            var detectors = new IIssueDetector[]
            {
                new StubDetector(
                    Issue("B_CODE", IssueSeverity.Warning, "titre Z"),
                    Issue("A_CODE", IssueSeverity.Warning, "titre A"),
                    Issue("A_CODE", IssueSeverity.Warning, "titre B"))
            };

            var result = IssueAggregator.Aggregate(new AuditSnapshot(), detectors);

            // Tri attendu : A_CODE/titre A, A_CODE/titre B, B_CODE/titre Z
            Assert.Equal("A_CODE",  result[0].Code);
            Assert.Equal("titre A", result[0].Title);
            Assert.Equal("A_CODE",  result[1].Code);
            Assert.Equal("titre B", result[1].Title);
            Assert.Equal("B_CODE",  result[2].Code);
        }

        [Fact]
        public void Null_detector_in_list_is_ignored()
        {
            var detectors = new IIssueDetector[]
            {
                null,
                new StubDetector(Issue("A", IssueSeverity.Info))
            };

            var result = IssueAggregator.Aggregate(new AuditSnapshot(), detectors);
            Assert.Single(result);
        }

        [Fact]
        public void Default_aggregator_runs_thirteen_detectors()
        {
            var defaults = IssueAggregator.BuildDefaultDetectors().ToList();
            // 7 détecteurs historiques (topologie/OS/réplication) + 4 DNS + 2 Ports = 13.
            // Ce test est volontairement strict pour forcer une réflexion explicite
            // à chaque ajout : tout nouveau détecteur doit-il être activé par défaut ?
            Assert.Equal(13, defaults.Count);
        }

        [Fact]
        public void End_to_end_detects_multiple_categories_at_once()
        {
            // Snapshot conçu pour déclencher au moins 3 détecteurs différents :
            // - OS obsolète sur DC02
            // - Site sans bridgehead (Lyon)
            // - Coût aberrant sur un site link
            var snapshot = new AuditSnapshot
            {
                DomainControllers = new[]
                {
                    new DomainControllerInfo { HostName = "DC01", Domain = "exemple.local", OSVersion = "Windows Server 2022" },
                    new DomainControllerInfo { HostName = "DC02", Domain = "exemple.local", OSVersion = "Windows Server 2008 R2" }
                },
                ReplicationLinks = new[]
                {
                    new ReplicationLink { SourceDc = "DC01", DestinationDc = "DC02" },
                    new ReplicationLink { SourceDc = "DC02", DestinationDc = "DC01" }
                },
                Sites = new[]
                {
                    new SiteInfo { Name = "Paris", DomainControllers = new[] { "DC01" }, Subnets = new[] { "10.0.0.0/24" }, BridgeheadServers = new[] { "DC01" } },
                    new SiteInfo { Name = "Lyon",  DomainControllers = new[] { "DC02" }, Subnets = new[] { "10.1.0.0/24" } }
                },
                SiteLinks = new[]
                {
                    new SiteLinkInfo { Name = "Paris-Lyon", Cost = -5 }
                }
            };

            var result = IssueAggregator.Aggregate(snapshot);

            Assert.Contains(result, i => i.Code == "OS_UNSUPPORTED");
            Assert.Contains(result, i => i.Code == "SITE_NO_BRIDGEHEAD");
            Assert.Contains(result, i => i.Code == "SITE_LINK_COST_ABERRANT");
            // Et l'ordre place le critique en tête.
            Assert.Equal(IssueSeverity.Critical, result[0].Severity);
        }
    }
}
