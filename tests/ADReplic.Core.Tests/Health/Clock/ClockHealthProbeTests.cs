using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Health.Clock;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Health.Clock
{
    /// <summary>
    /// Sonde d'horloge (v0.8.0) : lit currentTime dans le RootDSE LDAP de chaque DC
    /// (port 389, déjà requis, aucune dépendance) et mesure son décalage par rapport
    /// au poste d'audit. Les DC injoignables ne sont pas interrogés.
    /// </summary>
    public class ClockHealthProbeTests
    {
        private sealed class FakeReader : IDcClockReader
        {
            private readonly Dictionary<string, Func<DateTime?>> _answers = new Dictionary<string, Func<DateTime?>>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Asked = new List<string>();
            public FakeReader Returns(string host, Func<DateTime?> answer) { _answers[host] = answer; return this; }
            public Task<DateTime?> ReadCurrentTimeUtcAsync(string hostName, TimeSpan timeout, CancellationToken ct)
            {
                Asked.Add(hostName);
                return Task.FromResult(_answers.TryGetValue(hostName, out var f) ? f() : null);
            }
        }

        private static DomainControllerInfo Dc(string host, bool reachable = true)
            => new DomainControllerInfo { HostName = host, IsReachable = reachable };

        [Fact]
        public async Task Offset_is_dc_time_minus_audit_host_time()
        {
            var reader = new FakeReader().Returns("dc01.exemple.local", () => DateTime.UtcNow.AddMinutes(7));

            var result = await new ClockHealthProbe(reader).ProbeAsync(new[] { Dc("dc01.exemple.local") }, CancellationToken.None);

            var check = Assert.Single(result.Checks);
            Assert.Equal(ClockCheckStatus.Ok, check.Status);
            Assert.NotNull(check.OffsetFromAuditHost);
            Assert.InRange(check.OffsetFromAuditHost.Value.TotalMinutes, 6.9, 7.1);
        }

        [Fact]
        public async Task Unreachable_dcs_are_skipped_without_being_asked()
        {
            var reader = new FakeReader().Returns("dc01.exemple.local", () => DateTime.UtcNow);

            var result = await new ClockHealthProbe(reader).ProbeAsync(
                new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local", reachable: false) }, CancellationToken.None);

            Assert.Equal(2, result.Checks.Count);
            Assert.DoesNotContain("dc02.exemple.local", reader.Asked);
            var skipped = result.Checks.Single(c => c.HostName == "dc02.exemple.local");
            Assert.Equal(ClockCheckStatus.Skipped, skipped.Status);
            Assert.Null(skipped.OffsetFromAuditHost);
        }

        [Fact]
        public async Task A_dc_that_does_not_answer_is_reported_as_error_not_as_zero_offset()
        {
            var reader = new FakeReader().Returns("dc01.exemple.local", () => null);

            var result = await new ClockHealthProbe(reader).ProbeAsync(new[] { Dc("dc01.exemple.local") }, CancellationToken.None);

            var check = Assert.Single(result.Checks);
            Assert.Equal(ClockCheckStatus.Error, check.Status);
            Assert.Null(check.OffsetFromAuditHost);
        }

        [Fact]
        public async Task A_reader_exception_does_not_abort_the_other_dcs()
        {
            var reader = new FakeReader()
                .Returns("dc01.exemple.local", () => throw new InvalidOperationException("LDAP down"))
                .Returns("dc02.exemple.local", () => DateTime.UtcNow);

            var result = await new ClockHealthProbe(reader).ProbeAsync(
                new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local") }, CancellationToken.None);

            Assert.Equal(ClockCheckStatus.Error, result.Checks.Single(c => c.HostName == "dc01.exemple.local").Status);
            Assert.Contains("LDAP down", result.Checks.Single(c => c.HostName == "dc01.exemple.local").Detail);
            Assert.Equal(ClockCheckStatus.Ok, result.Checks.Single(c => c.HostName == "dc02.exemple.local").Status);
        }

        [Fact]
        public void Rootdse_generalized_time_is_parsed_as_utc()
        {
            var parsed = LdapRootDseClockReader.ParseGeneralizedTime("20261015181200.0Z");

            Assert.NotNull(parsed);
            Assert.Equal(new DateTime(2026, 10, 15, 18, 12, 0, DateTimeKind.Utc), parsed.Value);
            Assert.Equal(DateTimeKind.Utc, parsed.Value.Kind);
            Assert.Null(LdapRootDseClockReader.ParseGeneralizedTime("n'importe quoi"));
        }
    }
}
