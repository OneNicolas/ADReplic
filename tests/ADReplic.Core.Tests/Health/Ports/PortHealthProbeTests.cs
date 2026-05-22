using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Health.Ports
{
    public class PortHealthProbeTests
    {
        // ---- Fake prober ----

        /// <summary>
        /// Prober déterministe qui rejoue des outcomes préprogrammés selon (host, port).
        /// Si aucun outcome n'est défini pour une clé, retourne Open par défaut
        /// (réseau idéal — facilite l'écriture des tests centrés sur autre chose).
        /// </summary>
        private sealed class FakePortProber : IPortProber
        {
            private readonly Dictionary<string, PortProbeOutcome> _outcomes
                = new Dictionary<string, PortProbeOutcome>();

            public int CallCount { get; private set; }

            public void When(string host, int port, PortProbeOutcome outcome)
                => _outcomes[Key(host, port)] = outcome;

            public Task<PortProbeOutcome> TryConnectAsync(
                string hostName, int port, TimeSpan timeout, CancellationToken cancellationToken)
            {
                CallCount++;
                cancellationToken.ThrowIfCancellationRequested();

                if (_outcomes.TryGetValue(Key(hostName, port), out var outcome))
                    return Task.FromResult(outcome);

                return Task.FromResult(new PortProbeOutcome
                {
                    Status = PortCheckStatus.Open,
                    ResponseTime = TimeSpan.FromMilliseconds(1)
                });
            }

            private static string Key(string host, int port) => host + ":" + port;
        }

        private static PortProbeOutcome Outcome(PortCheckStatus status, int ms = 1, string err = null)
            => new PortProbeOutcome
            {
                Status = status,
                ResponseTime = TimeSpan.FromMilliseconds(ms),
                ErrorMessage = err
            };

        private static DomainControllerInfo Dc(string host)
            => new DomainControllerInfo { HostName = host };

        private static IReadOnlyList<AdServicePort> TwoPorts()
            => new[] { new AdServicePort(389, "LDAP"), new AdServicePort(636, "LDAPS") };

        // ---- Tests ----

        [Fact]
        public async Task Produces_one_check_per_dc_x_port_combination()
        {
            var prober = new FakePortProber();
            var probe = new PortHealthProbe(prober);

            var result = await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local") },
                TwoPorts(),
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            Assert.Equal(4, result.Checks.Count);
            Assert.Equal(4, prober.CallCount);
        }

        [Fact]
        public async Task Uses_default_port_list_when_none_provided()
        {
            var prober = new FakePortProber();
            var probe = new PortHealthProbe(prober);

            var result = await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local") },
                portsToCheck: null,
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            Assert.Equal(AdServicePorts.Default.Count, result.Checks.Count);
        }

        [Fact]
        public async Task Maps_outcome_status_and_response_time_into_check_result()
        {
            var prober = new FakePortProber();
            prober.When("dc01.exemple.local", 389, Outcome(PortCheckStatus.Open, ms: 42));
            prober.When("dc01.exemple.local", 636, Outcome(PortCheckStatus.Closed, ms: 7));

            var probe = new PortHealthProbe(prober);
            var result = await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local") },
                TwoPorts(),
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            var ldap = result.Checks.Single(c => c.Port == 389);
            Assert.Equal(PortCheckStatus.Open, ldap.Status);
            Assert.Equal(42, (int)ldap.ResponseTime.TotalMilliseconds);
            Assert.Equal("LDAP", ldap.ServiceLabel);

            var ldaps = result.Checks.Single(c => c.Port == 636);
            Assert.Equal(PortCheckStatus.Closed, ldaps.Status);
        }

        [Fact]
        public async Task Carries_error_message_when_outcome_is_error()
        {
            var prober = new FakePortProber();
            prober.When("dc01.exemple.local", 389, Outcome(PortCheckStatus.Error, err: "DNS résolution KO"));

            var probe = new PortHealthProbe(prober);
            var result = await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local") },
                new[] { new AdServicePort(389, "LDAP") },
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            var check = Assert.Single(result.Checks);
            Assert.Equal(PortCheckStatus.Error, check.Status);
            Assert.Equal("DNS résolution KO", check.ErrorMessage);
        }

        [Fact]
        public async Task Sorts_checks_by_hostname_then_port()
        {
            var prober = new FakePortProber();
            var probe = new PortHealthProbe(prober);

            var result = await probe.ProbeAsync(
                new[] { Dc("dc02.exemple.local"), Dc("dc01.exemple.local") },
                new[] { new AdServicePort(636, "LDAPS"), new AdServicePort(389, "LDAP") },
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            // 4 entrées attendues dans l'ordre dc01:389, dc01:636, dc02:389, dc02:636
            Assert.Equal("dc01.exemple.local", result.Checks[0].HostName);
            Assert.Equal(389, result.Checks[0].Port);
            Assert.Equal("dc01.exemple.local", result.Checks[1].HostName);
            Assert.Equal(636, result.Checks[1].Port);
            Assert.Equal("dc02.exemple.local", result.Checks[2].HostName);
            Assert.Equal(389, result.Checks[2].Port);
            Assert.Equal("dc02.exemple.local", result.Checks[3].HostName);
            Assert.Equal(636, result.Checks[3].Port);
        }

        [Fact]
        public async Task Ignores_dc_with_null_or_blank_hostname()
        {
            var prober = new FakePortProber();
            var probe = new PortHealthProbe(prober);

            var result = await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local"), Dc(""), Dc(null), null },
                TwoPorts(),
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            Assert.Equal(2, result.Checks.Count); // 1 DC valide × 2 ports
            Assert.All(result.Checks, c => Assert.Equal("dc01.exemple.local", c.HostName));
        }

        [Fact]
        public async Task Accepts_null_dc_list_and_returns_empty_checks()
        {
            var prober = new FakePortProber();
            var probe = new PortHealthProbe(prober);

            var result = await probe.ProbeAsync(
                null, TwoPorts(),
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            Assert.Empty(result.Checks);
            Assert.Equal(0, prober.CallCount);
        }

        [Fact]
        public async Task Falls_back_to_default_timeout_when_caller_passes_zero_or_negative()
        {
            // Pour ce test on capture le timeout effectivement reçu par le prober.
            TimeSpan? observed = null;
            var prober = new InspectingProber(t => observed = t);

            var probe = new PortHealthProbe(prober);
            await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local") },
                new[] { new AdServicePort(389, "LDAP") },
                TimeSpan.Zero,
                CancellationToken.None);

            Assert.True(observed.HasValue);
            Assert.Equal(PortHealthProbe.DefaultPerPortTimeout, observed.Value);
        }

        [Fact]
        public async Task Propagates_caller_provided_timeout_to_prober()
        {
            TimeSpan? observed = null;
            var prober = new InspectingProber(t => observed = t);

            var probe = new PortHealthProbe(prober);
            await probe.ProbeAsync(
                new[] { Dc("dc01.exemple.local") },
                new[] { new AdServicePort(389, "LDAP") },
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(5), observed);
        }

        [Fact]
        public void Throws_when_prober_is_null()
        {
            Assert.Throws<ArgumentNullException>(() => new PortHealthProbe(null));
        }

        [Fact]
        public async Task Captured_at_is_utc_and_recent()
        {
            var prober = new FakePortProber();
            var probe = new PortHealthProbe(prober);

            var before = DateTime.UtcNow;
            var result = await probe.ProbeAsync(
                Array.Empty<DomainControllerInfo>(), TwoPorts(),
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
            var after = DateTime.UtcNow;

            Assert.InRange(result.CapturedAtUtc, before.AddSeconds(-1), after.AddSeconds(1));
            Assert.Equal(DateTimeKind.Utc, result.CapturedAtUtc.Kind);
        }

        /// <summary>Prober qui se contente de noter le timeout reçu — utile pour vérifier le mapping côté Probe.</summary>
        private sealed class InspectingProber : IPortProber
        {
            private readonly Action<TimeSpan> _onCall;
            public InspectingProber(Action<TimeSpan> onCall) { _onCall = onCall; }

            public Task<PortProbeOutcome> TryConnectAsync(
                string hostName, int port, TimeSpan timeout, CancellationToken cancellationToken)
            {
                _onCall(timeout);
                return Task.FromResult(new PortProbeOutcome { Status = PortCheckStatus.Open });
            }
        }
    }
}
