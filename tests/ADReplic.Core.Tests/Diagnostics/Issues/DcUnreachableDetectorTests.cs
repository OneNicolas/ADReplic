using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class DcUnreachableDetectorTests
    {
        private static DomainControllerInfo Dc(string host, bool reachable = true) =>
            new DomainControllerInfo { HostName = host, Domain = "exemple.local", IsReachable = reachable };

        [Fact]
        public void Silent_when_snapshot_is_null()
        {
            Assert.Empty(new DcUnreachableDetector().Detect(null));
        }

        [Fact]
        public void Silent_when_dc_list_is_null()
        {
            Assert.Empty(new DcUnreachableDetector().Detect(new AuditSnapshot()));
        }

        [Fact]
        public void Silent_when_all_dcs_are_reachable()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local") }
            };

            Assert.Empty(new DcUnreachableDetector().Detect(snap));
        }

        [Fact]
        public void Emits_critical_issue_for_unreachable_dc()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[]
                {
                    Dc("dc01.exemple.local"),
                    Dc("dc02.exemple.local", reachable: false)
                }
            };

            var issue = Assert.Single(new DcUnreachableDetector().Detect(snap));
            Assert.Equal("DC_UNREACHABLE", issue.Code);
            Assert.Equal(IssueSeverity.Critical, issue.Severity);
            Assert.Contains("dc02.exemple.local", issue.AffectedItems);
        }

        [Fact]
        public void Emits_one_issue_per_unreachable_dc()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[]
                {
                    Dc("dc01.exemple.local", reachable: false),
                    Dc("dc02.exemple.local", reachable: false),
                    Dc("dc03.exemple.local")
                }
            };

            Assert.Equal(2, new DcUnreachableDetector().Detect(snap).Count());
        }

        [Fact]
        public void Ignores_dc_without_hostname()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc(null, reachable: false), Dc("", reachable: false) }
            };

            Assert.Empty(new DcUnreachableDetector().Detect(snap));
        }

        [Fact]
        public void DomainControllerInfo_defaults_to_reachable()
        {
            // Garantit la rétro-compatibilité des JSON v0.5.0 (sans le champ) relus
            // par JsonAuditReader : propriété absente => défaut du modèle => joignable.
            Assert.True(new DomainControllerInfo().IsReachable);
        }
    }
}
