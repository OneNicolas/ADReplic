using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Health.Dns;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Health.Dns
{
    public class DnsHealthProbeTests
    {
        // ---- Fake resolver ----

        /// <summary>
        /// Résolveur déterministe qui rejoue des réponses préprogrammées en fonction
        /// du couple (nom, type). Si aucune réponse n'est définie, retourne Missing.
        /// </summary>
        private sealed class FakeDnsResolver : IDnsResolver
        {
            private readonly Dictionary<string, DnsQueryResult> _responses
                = new Dictionary<string, DnsQueryResult>();

            public void When(string name, DnsQueryRecordType type, DnsQueryResult response)
                => _responses[Key(name, type)] = response;

            public DnsQueryResult Query(string name, DnsQueryRecordType type)
            {
                return _responses.TryGetValue(Key(name, type), out var r)
                    ? r
                    : new DnsQueryResult { Status = DnsQueryStatus.Missing, ErrorCode = 9003 };
            }

            private static string Key(string name, DnsQueryRecordType type) => type + "|" + name;
        }

        private static DnsQueryResult OkSrv(string target, int port, ushort priority = 0, ushort weight = 100)
        {
            return new DnsQueryResult
            {
                Status = DnsQueryStatus.Ok,
                ErrorCode = 0,
                Records = new[]
                {
                    new DnsRawRecord { Target = target, Port = port, Priority = priority, Weight = weight }
                }
            };
        }

        private static DnsQueryResult OkA(string ip)
        {
            return new DnsQueryResult
            {
                Status = DnsQueryStatus.Ok,
                ErrorCode = 0,
                Records = new[] { new DnsRawRecord { IpAddress = ip } }
            };
        }

        private static DnsQueryResult ErrorResult(int code)
            => new DnsQueryResult { Status = DnsQueryStatus.Error, ErrorCode = code };

        private static DomainControllerInfo Dc(string host)
            => new DomainControllerInfo { HostName = host };

        // ---- Tests ----

        [Fact]
        public async Task Produces_one_check_per_srv_and_one_per_dc_a_record()
        {
            var resolver = new FakeDnsResolver();
            var probe = new DnsHealthProbe(resolver);

            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                new[] { Dc("dc01.exemple.local"), Dc("dc02.exemple.local") },
                CancellationToken.None);

            // 4 SRV + 2 A = au moins 6 lignes
            Assert.True(result.Checks.Count >= 6);
            Assert.Contains(result.Checks, c => c.Type == DnsCheckedRecordType.SrvLdap);
            Assert.Contains(result.Checks, c => c.Type == DnsCheckedRecordType.SrvKerberos);
            Assert.Contains(result.Checks, c => c.Type == DnsCheckedRecordType.SrvGc);
            Assert.Contains(result.Checks, c => c.Type == DnsCheckedRecordType.SrvKpasswd);
            Assert.Equal(2, result.Checks.Count(c => c.Type == DnsCheckedRecordType.ARecord));
        }

        [Fact]
        public async Task Maps_resolver_ok_to_check_status_ok_with_typed_fields()
        {
            var resolver = new FakeDnsResolver();
            resolver.When("_ldap._tcp.dc._msdcs.exemple.local", DnsQueryRecordType.Srv,
                OkSrv("dc01.exemple.local", 389, priority: 0, weight: 100));
            resolver.When("dc01.exemple.local", DnsQueryRecordType.A,
                OkA("10.1.2.3"));

            var probe = new DnsHealthProbe(resolver);
            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                new[] { Dc("dc01.exemple.local") },
                CancellationToken.None);

            var ldap = result.Checks.Single(c => c.Type == DnsCheckedRecordType.SrvLdap);
            Assert.Equal(DnsCheckStatus.Ok, ldap.Status);
            Assert.Equal("dc01.exemple.local", ldap.Target);
            Assert.Equal(389, ldap.Port);
            Assert.Equal((ushort)0, ldap.Priority);
            Assert.Equal((ushort)100, ldap.Weight);
            Assert.Null(ldap.IpAddress);

            var aRecord = result.Checks.Single(c => c.Type == DnsCheckedRecordType.ARecord);
            Assert.Equal(DnsCheckStatus.Ok, aRecord.Status);
            Assert.Equal("dc01.exemple.local", aRecord.Target);
            Assert.Equal("10.1.2.3", aRecord.IpAddress);
            Assert.Null(aRecord.Port);
        }

        [Fact]
        public async Task Maps_resolver_missing_to_check_status_missing_with_no_target()
        {
            var resolver = new FakeDnsResolver();
            // Aucun When() : le fake répond Missing par défaut.

            var probe = new DnsHealthProbe(resolver);
            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                new[] { Dc("dc01.exemple.local") },
                CancellationToken.None);

            Assert.All(result.Checks, c =>
            {
                Assert.Equal(DnsCheckStatus.Missing, c.Status);
                Assert.Null(c.Target);
                Assert.NotEqual(0, c.ErrorCode);
            });
        }

        [Fact]
        public async Task Maps_resolver_error_to_check_status_error_with_code()
        {
            var resolver = new FakeDnsResolver();
            resolver.When("_ldap._tcp.dc._msdcs.exemple.local", DnsQueryRecordType.Srv,
                ErrorResult(9002)); // SERVFAIL

            var probe = new DnsHealthProbe(resolver);
            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                new[] { Dc("dc01.exemple.local") },
                CancellationToken.None);

            var ldap = result.Checks.Single(c => c.Type == DnsCheckedRecordType.SrvLdap);
            Assert.Equal(DnsCheckStatus.Error, ldap.Status);
            Assert.Equal(9002, ldap.ErrorCode);
        }

        [Fact]
        public async Task Emits_one_row_per_returned_record_when_srv_has_multiple_targets()
        {
            var resolver = new FakeDnsResolver();
            resolver.When("_ldap._tcp.dc._msdcs.exemple.local", DnsQueryRecordType.Srv,
                new DnsQueryResult
                {
                    Status = DnsQueryStatus.Ok,
                    Records = new[]
                    {
                        new DnsRawRecord { Target = "dc01.exemple.local", Port = 389, Priority = 0, Weight = 100 },
                        new DnsRawRecord { Target = "dc02.exemple.local", Port = 389, Priority = 0, Weight = 100 },
                        new DnsRawRecord { Target = "dc03.exemple.local", Port = 389, Priority = 0, Weight = 100 },
                    }
                });

            var probe = new DnsHealthProbe(resolver);
            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                System.Array.Empty<DomainControllerInfo>(),
                CancellationToken.None);

            var ldapChecks = result.Checks.Where(c => c.Type == DnsCheckedRecordType.SrvLdap).ToList();
            Assert.Equal(3, ldapChecks.Count);
            Assert.Contains(ldapChecks, c => c.Target == "dc01.exemple.local");
            Assert.Contains(ldapChecks, c => c.Target == "dc02.exemple.local");
            Assert.Contains(ldapChecks, c => c.Target == "dc03.exemple.local");
        }

        [Fact]
        public async Task Ignores_dc_with_null_or_blank_hostname()
        {
            var resolver = new FakeDnsResolver();
            var probe = new DnsHealthProbe(resolver);

            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                new[] { Dc("dc01.exemple.local"), Dc(""), Dc(null), null },
                CancellationToken.None);

            Assert.Equal(1, result.Checks.Count(c => c.Type == DnsCheckedRecordType.ARecord));
        }

        [Fact]
        public async Task Accepts_empty_dc_list_and_still_probes_srv_records()
        {
            var resolver = new FakeDnsResolver();
            var probe = new DnsHealthProbe(resolver);

            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                System.Array.Empty<DomainControllerInfo>(),
                CancellationToken.None);

            Assert.Empty(result.Checks.Where(c => c.Type == DnsCheckedRecordType.ARecord));
            Assert.NotEmpty(result.Checks.Where(c => c.Type != DnsCheckedRecordType.ARecord));
        }

        [Fact]
        public async Task Accepts_null_dc_list()
        {
            var resolver = new FakeDnsResolver();
            var probe = new DnsHealthProbe(resolver);

            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                null,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Empty(result.Checks.Where(c => c.Type == DnsCheckedRecordType.ARecord));
        }

        [Fact]
        public async Task Sorts_checks_by_category_then_recordname_then_target()
        {
            var resolver = new FakeDnsResolver();
            resolver.When("_ldap._tcp.dc._msdcs.exemple.local", DnsQueryRecordType.Srv,
                new DnsQueryResult
                {
                    Status = DnsQueryStatus.Ok,
                    Records = new[]
                    {
                        new DnsRawRecord { Target = "dc02.exemple.local", Port = 389 },
                        new DnsRawRecord { Target = "dc01.exemple.local", Port = 389 },
                    }
                });

            var probe = new DnsHealthProbe(resolver);
            var result = await probe.ProbeAsync(
                "exemple.local", "exemple.local",
                System.Array.Empty<DomainControllerInfo>(),
                CancellationToken.None);

            var ldapChecks = result.Checks.Where(c => c.Type == DnsCheckedRecordType.SrvLdap).ToList();
            // Sort par Target une fois le Type et RecordName identiques.
            Assert.Equal("dc01.exemple.local", ldapChecks[0].Target);
            Assert.Equal("dc02.exemple.local", ldapChecks[1].Target);
        }

        [Fact]
        public void Throws_when_resolver_is_null()
        {
            Assert.Throws<System.ArgumentNullException>(() => new DnsHealthProbe(null));
        }

        [Fact]
        public async Task Result_captured_at_is_utc_and_recent()
        {
            var resolver = new FakeDnsResolver();
            var probe = new DnsHealthProbe(resolver);

            var before = System.DateTime.UtcNow;
            var result = await probe.ProbeAsync("exemple.local", "exemple.local",
                System.Array.Empty<DomainControllerInfo>(), CancellationToken.None);
            var after = System.DateTime.UtcNow;

            Assert.InRange(result.CapturedAtUtc, before.AddSeconds(-1), after.AddSeconds(1));
            Assert.Equal(System.DateTimeKind.Utc, result.CapturedAtUtc.Kind);
        }
    }
}
