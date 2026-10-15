using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// RelatedHost est le pivot de l'agrégation (IssueGrouper) : chaque détecteur
    /// rattaché à un DC précis doit le renseigner, les autres le laissent null.
    /// </summary>
    public class IssueRelatedHostTests
    {
        private static PortCheckResult Port(string host, int port, PortCheckStatus status)
            => new PortCheckResult { HostName = host, Port = port, Status = status, ServiceLabel = "svc" };

        [Fact]
        public void DcUnreachable_sets_related_host_to_the_dc()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { new DomainControllerInfo { HostName = "dc02.exemple.local", IsReachable = false } }
            };

            var issue = new DcUnreachableDetector().Detect(snap).Single();

            Assert.Equal("dc02.exemple.local", issue.RelatedHost);
        }

        [Fact]
        public void PortCriticalClosed_sets_related_host_to_the_probed_host()
        {
            var snap = new AuditSnapshot { PortHealth = new PortHealthResult { Checks = new[] { Port("dc02.exemple.local", 389, PortCheckStatus.Timeout) } } };

            var issue = new PortCriticalClosedDetector().Detect(snap).Single();

            Assert.Equal("dc02.exemple.local", issue.RelatedHost);
        }

        [Fact]
        public void PortClosedOrFiltered_sets_related_host_to_the_probed_host()
        {
            var snap = new AuditSnapshot { PortHealth = new PortHealthResult { Checks = new[] { Port("dc02.exemple.local", 636, PortCheckStatus.Closed) } } };

            var issue = new PortClosedOrFilteredDetector().Detect(snap).Single();

            Assert.Equal("dc02.exemple.local", issue.RelatedHost);
        }

        [Fact]
        public void DnsSrvPointsToDeadDc_sets_related_host_to_the_dead_target_without_trailing_dot()
        {
            var snap = new AuditSnapshot
            {
                DnsHealth = new DnsHealthResult
                {
                    Checks = new[] { new DnsCheckResult { RecordName = "_ldap._tcp.dc._msdcs.exemple.local", Type = DnsCheckedRecordType.SrvLdap, Target = "dc02.exemple.local.", Status = DnsCheckStatus.Ok } }
                },
                PortHealth = new PortHealthResult
                {
                    Checks = new[] { Port("dc02.exemple.local", 389, PortCheckStatus.Timeout), Port("dc02.exemple.local", 88, PortCheckStatus.Timeout) }
                }
            };

            var issue = new DnsSrvPointsToDeadDcDetector().Detect(snap).Single();

            Assert.Equal("dc02.exemple.local", issue.RelatedHost);
        }

        [Fact]
        public void Topology_detectors_leave_related_host_null()
        {
            var snap = new AuditSnapshot
            {
                Sites = new[] { new SiteInfo { Name = "Paris" } },
                DomainControllers = new DomainControllerInfo[0]
            };

            var issue = new SiteNoDcDetector().Detect(snap).Single();

            Assert.Null(issue.RelatedHost);
        }
    }
}
