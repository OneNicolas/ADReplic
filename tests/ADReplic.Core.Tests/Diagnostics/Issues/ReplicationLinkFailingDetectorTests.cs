using System;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class ReplicationLinkFailingDetectorTests
    {
        private static readonly DateTime Attempt = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        private static DomainControllerInfo Dc(string host, bool reachable = true) =>
            new DomainControllerInfo { HostName = host, Domain = "exemple.local", IsReachable = reachable };

        private static ReplicationLink Link(
            string destination, string source, string nc,
            ReplicationLinkStatus status = ReplicationLinkStatus.Failing,
            TimeSpan? sinceSuccess = null,
            int failures = 3,
            int code = 1722,
            string message = "Le serveur RPC n'est pas disponible.")
        {
            return new ReplicationLink
            {
                DestinationDc = destination,
                SourceDc = source,
                NamingContext = nc,
                Status = status,
                LastAttempt = Attempt,
                LastSuccess = sinceSuccess.HasValue ? Attempt - sinceSuccess.Value : (DateTime?)null,
                ConsecutiveFailures = failures,
                LastResultCode = code,
                LastResultMessage = message
            };
        }

        private static AuditSnapshot Snapshot(DomainControllerInfo[] dcs, params ReplicationLink[] links) =>
            new AuditSnapshot { DomainControllers = dcs, ReplicationLinks = links };

        private static readonly DomainControllerInfo[] TwoHealthyDcs =
            { Dc("dc01.exemple.local"), Dc("dc02.exemple.local") };

        [Fact]
        public void Silent_when_snapshot_is_null()
        {
            Assert.Empty(new ReplicationLinkFailingDetector().Detect(null));
        }

        [Fact]
        public void Silent_when_links_are_null()
        {
            Assert.Empty(new ReplicationLinkFailingDetector().Detect(new AuditSnapshot()));
        }

        [Theory]
        [InlineData(ReplicationLinkStatus.Healthy)]
        [InlineData(ReplicationLinkStatus.Warning)]
        [InlineData(ReplicationLinkStatus.Unknown)]
        [InlineData(ReplicationLinkStatus.Unreachable)]
        public void Silent_when_no_link_is_failing(ReplicationLinkStatus status)
        {
            // Warning couvre les rattrapages normaux apres un redemarrage : le detecteur
            // ne doit pas dupliquer l'onglet Replication ni faire de bruit a ce moment-la.
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", status, TimeSpan.FromHours(1), failures: 1));

            Assert.Empty(new ReplicationLinkFailingDetector().Detect(snap));
        }

        [Fact]
        public void Emits_critical_issue_for_failing_link()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Equal("REPLICATION_LINK_FAILING", issue.Code);
            Assert.Equal(IssueSeverity.Critical, issue.Severity);
            Assert.Equal(new[] { "dc02.exemple.local", "dc01.exemple.local" }, issue.AffectedItems);
            Assert.Equal("dc02.exemple.local", issue.RelatedHost);
            Assert.Contains("dc02.exemple.local", issue.Title);
            Assert.Contains("dc01.exemple.local", issue.Title);
        }

        [Fact]
        public void Title_mentions_time_since_last_success()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Contains("5 h", issue.Title);
        }

        [Fact]
        public void Title_says_never_succeeded_when_no_last_success()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: null));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Contains("jamais", issue.Title, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Groups_partitions_of_same_pair_into_one_issue()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)),
                Link("dc02.exemple.local", "dc01.exemple.local", "CN=Configuration,DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(2)),
                Link("dc02.exemple.local", "dc01.exemple.local", "CN=Schema,CN=Configuration,DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(4)));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Contains("3 partitions", issue.Title);
            Assert.Contains("CN=Configuration,DC=exemple,DC=local", issue.Description);
            Assert.Contains("CN=Schema,CN=Configuration,DC=exemple,DC=local", issue.Description);
            // La duree affichee est la plus ancienne absence de succes du couple.
            Assert.Contains("5 h", issue.Title);
        }

        [Fact]
        public void Only_failing_partitions_of_a_pair_are_counted()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)),
                Link("dc02.exemple.local", "dc01.exemple.local", "CN=Configuration,DC=exemple,DC=local",
                     status: ReplicationLinkStatus.Healthy, sinceSuccess: TimeSpan.Zero, failures: 0, code: 0, message: null));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Contains("1 partition", issue.Title);
            Assert.DoesNotContain("CN=Configuration", issue.Description);
        }

        [Fact]
        public void Emits_one_issue_per_direction()
        {
            // dc02 <- dc01 et dc01 <- dc02 sont deux liens distincts : chaque sens
            // a ses propres causes (pare-feu asymetrique, SPN, etc.).
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)),
                Link("dc01.exemple.local", "dc02.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            var issues = new ReplicationLinkFailingDetector().Detect(snap).ToList();

            Assert.Equal(2, issues.Count);
            Assert.Contains(issues, i => i.RelatedHost == "dc02.exemple.local");
            Assert.Contains(issues, i => i.RelatedHost == "dc01.exemple.local");
        }

        [Fact]
        public void Description_mentions_failure_count_and_last_error()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local",
                     sinceSuccess: TimeSpan.FromHours(5), failures: 7, code: 1722, message: "Le serveur RPC n'est pas disponible."));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Contains("7", issue.Description);
            Assert.Contains("1722", issue.Description);
            Assert.Contains("Le serveur RPC n'est pas disponible.", issue.Description);
        }

        [Fact]
        public void Ignores_links_whose_source_is_an_unreachable_dc()
        {
            // Deduplication avec DC_UNREACHABLE : la cause est deja nommee par
            // l'anomalie chapeau, inutile de la repeter pour chaque partenaire.
            var dcs = new[] { Dc("dc01.exemple.local", reachable: false), Dc("dc02.exemple.local") };
            var snap = Snapshot(dcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            Assert.Empty(new ReplicationLinkFailingDetector().Detect(snap));
        }

        [Fact]
        public void Ignores_links_whose_destination_is_an_unreachable_dc()
        {
            // Cas d'un JSON ou le statut Failing aurait ete fige avant que le DC ne tombe.
            var dcs = new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local", reachable: false) };
            var snap = Snapshot(dcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            Assert.Empty(new ReplicationLinkFailingDetector().Detect(snap));
        }

        [Fact]
        public void Unreachable_dc_matching_ignores_case_and_trailing_dot()
        {
            var dcs = new[] { Dc("DC01.EXEMPLE.LOCAL.", reachable: false), Dc("dc02.exemple.local") };
            var snap = Snapshot(dcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            Assert.Empty(new ReplicationLinkFailingDetector().Detect(snap));
        }

        [Fact]
        public void Still_detects_failing_links_between_reachable_dcs_when_another_dc_is_down()
        {
            var dcs = new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local"), Dc("dc03.exemple.local", reachable: false) };
            var snap = Snapshot(dcs,
                Link("dc02.exemple.local", "dc03.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)),
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            var issue = Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));

            Assert.Equal(new[] { "dc02.exemple.local", "dc01.exemple.local" }, issue.AffectedItems);
        }

        [Fact]
        public void Pair_grouping_ignores_host_name_case()
        {
            var snap = Snapshot(TwoHealthyDcs,
                Link("dc02.exemple.local", "dc01.exemple.local", "DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)),
                Link("DC02.exemple.local", "DC01.exemple.local", "CN=Configuration,DC=exemple,DC=local", sinceSuccess: TimeSpan.FromHours(5)));

            Assert.Single(new ReplicationLinkFailingDetector().Detect(snap));
        }
    }
}
