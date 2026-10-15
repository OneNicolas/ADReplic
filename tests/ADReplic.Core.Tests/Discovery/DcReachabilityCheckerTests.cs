using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Discovery;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;
using Xunit;

namespace ADReplic.Core.Tests.Discovery
{
    /// <summary>
    /// Découverte rapide (v0.8.0) : un DC éteint coûtait 2 à 3 minutes (3 lectures AD
    /// en timeout, puis deux sondes qui retentaient). Le pré-test TCP 389 tranche en
    /// 2 secondes, et les sondes ne contactent plus un DC déjà marqué injoignable.
    /// </summary>
    public class DcReachabilityCheckerTests
    {
        private sealed class FakeProber : IPortProber
        {
            private readonly PortCheckStatus _status;
            public string LastHost; public int LastPort; public TimeSpan LastTimeout;
            public FakeProber(PortCheckStatus status) { _status = status; }
            public Task<PortProbeOutcome> TryConnectAsync(string hostName, int port, TimeSpan timeout, CancellationToken ct)
            {
                LastHost = hostName; LastPort = port; LastTimeout = timeout;
                return Task.FromResult(new PortProbeOutcome { Status = _status });
            }
        }

        [Fact]
        public void Open_ldap_port_means_reachable()
        {
            var prober = new FakeProber(PortCheckStatus.Open);

            var reachable = new DcReachabilityChecker(prober).IsReachable("dc01.exemple.local", CancellationToken.None);

            Assert.True(reachable);
            Assert.Equal("dc01.exemple.local", prober.LastHost);
            Assert.Equal(389, prober.LastPort);
            Assert.True(prober.LastTimeout <= TimeSpan.FromSeconds(2));
        }

        [Theory]
        [InlineData(PortCheckStatus.Timeout)]
        [InlineData(PortCheckStatus.Closed)]
        [InlineData(PortCheckStatus.Error)]
        public void Anything_but_open_means_unreachable(PortCheckStatus status)
        {
            // Un RST sur 389 (Closed) signifie "machine allumée mais LDAP absent" : les
            // lectures AD échoueraient aussi, inutile de les attendre.
            var reachable = new DcReachabilityChecker(new FakeProber(status)).IsReachable("dc02.exemple.local", CancellationToken.None);

            Assert.False(reachable);
        }

        [Fact]
        public async Task Replication_probe_does_not_contact_a_dc_already_marked_unreachable()
        {
            var deadDc = new DomainControllerInfo { HostName = "dc-eteint.exemple.invalid", IsReachable = false };
            var sw = Stopwatch.StartNew();

            var links = await new ReplicationProbe().ProbeAsync(new[] { deadDc }, new AuditContext(), CancellationToken.None);

            var link = Assert.Single(links);
            Assert.Equal(ReplicationLinkStatus.Unreachable, link.Status);
            Assert.Equal("dc-eteint.exemple.invalid", link.DestinationDc);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), "la sonde a contacté le réseau");
        }

        [Fact]
        public async Task Failure_probe_does_not_contact_a_dc_already_marked_unreachable()
        {
            var deadDc = new DomainControllerInfo { HostName = "dc-eteint.exemple.invalid", IsReachable = false };
            var sw = Stopwatch.StartNew();

            var failures = await new ReplicationFailureProbe().GetFailuresAsync(new[] { deadDc }, new AuditContext(), CancellationToken.None);

            var failure = Assert.Single(failures);
            Assert.Equal(ReplicationFailureProbe.UnreachableErrorCode, failure.LastErrorCode);
            Assert.Equal(ReplicationFailureSeverity.Critical, failure.Severity);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), "la sonde a contacté le réseau");
        }
    }
}
