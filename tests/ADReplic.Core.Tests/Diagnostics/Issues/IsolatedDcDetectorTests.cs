using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class IsolatedDcDetectorTests
    {
        private static DomainControllerInfo Dc(string name) => new DomainControllerInfo { HostName = name };

        private static ReplicationLink Link(string src, string dst) =>
            new ReplicationLink { SourceDc = src, DestinationDc = dst };

        private static AuditSnapshot Snapshot(
            IReadOnlyList<DomainControllerInfo> dcs,
            IReadOnlyList<ReplicationLink> links) =>
            new AuditSnapshot
            {
                DomainControllers = dcs,
                ReplicationLinks  = links
            };

        [Fact]
        public void Returns_nothing_when_snapshot_is_null()
        {
            var issues = new IsolatedDcDetector().Detect(null).ToList();
            Assert.Empty(issues);
        }

        [Fact]
        public void Returns_nothing_when_only_one_dc()
        {
            // Un seul DC est traité par SingleDcDomainDetector, pas ici.
            var snap = Snapshot(new[] { Dc("DC01") }, new ReplicationLink[0]);
            var issues = new IsolatedDcDetector().Detect(snap).ToList();
            Assert.Empty(issues);
        }

        [Fact]
        public void Returns_nothing_when_no_links_observed()
        {
            // Aucun lien observé : sonde probablement HS, on ne déduit rien.
            var snap = Snapshot(new[] { Dc("DC01"), Dc("DC02") }, new ReplicationLink[0]);
            var issues = new IsolatedDcDetector().Detect(snap).ToList();
            Assert.Empty(issues);
        }

        [Fact]
        public void Returns_nothing_when_all_dcs_have_links()
        {
            var snap = Snapshot(
                new[] { Dc("DC01"), Dc("DC02"), Dc("DC03") },
                new[]
                {
                    Link("DC01", "DC02"),
                    Link("DC02", "DC03"),
                    Link("DC03", "DC01")
                });

            var issues = new IsolatedDcDetector().Detect(snap).ToList();
            Assert.Empty(issues);
        }

        [Fact]
        public void Flags_a_dc_absent_from_all_links()
        {
            // DC03 n'apparaît ni en source ni en destination.
            var snap = Snapshot(
                new[] { Dc("DC01"), Dc("DC02"), Dc("DC03") },
                new[] { Link("DC01", "DC02"), Link("DC02", "DC01") });

            var issues = new IsolatedDcDetector().Detect(snap).ToList();

            Assert.Single(issues);
            Assert.Equal("DC_ISOLATED",          issues[0].Code);
            Assert.Equal(IssueSeverity.Critical, issues[0].Severity);
            Assert.Contains("DC03",              issues[0].AffectedItems);
        }

        [Fact]
        public void Flags_multiple_isolated_dcs()
        {
            var snap = Snapshot(
                new[] { Dc("DC01"), Dc("DC02"), Dc("DC03"), Dc("DC04") },
                new[] { Link("DC01", "DC02") });

            var issues = new IsolatedDcDetector().Detect(snap).ToList();

            Assert.Equal(2, issues.Count);
            Assert.Contains(issues, i => i.AffectedItems.Contains("DC03"));
            Assert.Contains(issues, i => i.AffectedItems.Contains("DC04"));
        }

        [Fact]
        public void Detection_is_case_insensitive()
        {
            // Le casing peut varier entre l'inventaire et les liens (NetBIOS vs FQDN).
            var snap = Snapshot(
                new[] { Dc("dc01.exemple.local"), Dc("DC02.EXEMPLE.LOCAL") },
                new[] { Link("DC01.exemple.local", "dc02.exemple.local") });

            var issues = new IsolatedDcDetector().Detect(snap).ToList();
            Assert.Empty(issues);
        }

        [Fact]
        public void Returns_nothing_in_single_dc_mode()
        {
            // Mode DC seul : on a interrogé un seul DC, donc l'absence de lien
            // dans le snapshot n'a aucune valeur d'isolation.
            var snap = Snapshot(
                new[] { Dc("DC01"), Dc("DC02"), Dc("DC03") },
                new[] { Link("DC01", "DC02") });
            snap.IsSingleDcMode = true;

            Assert.Empty(new IsolatedDcDetector().Detect(snap).ToList());
        }
    }
}
