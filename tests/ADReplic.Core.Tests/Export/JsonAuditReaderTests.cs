using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Export
{
    /// <summary>
    /// Vérifie que JsonAuditReader peut désérialiser ce que JsonAuditExporter
    /// produit, sur des snapshots de complexité croissante. Le round-trip est
    /// le filet de sécurité principal : si l'exporter écrit X et le reader
    /// reconstruit X, le couple est cohérent par construction.
    /// </summary>
    public class JsonAuditReaderTests : IDisposable
    {
        private readonly string _tempFile;

        public JsonAuditReaderTests()
        {
            _tempFile = Path.Combine(
                Path.GetTempPath(),
                "adreplic-roundtrip-" + Guid.NewGuid().ToString("N") + ".json");
        }

        public void Dispose()
        {
            try { if (File.Exists(_tempFile)) File.Delete(_tempFile); }
            catch { /* best-effort */ }
        }

        private static AuditSnapshot RoundTrip(AuditSnapshot input, string path)
        {
            new JsonAuditExporter().Export(input, path);
            return new JsonAuditReader().Read(path);
        }

        [Fact]
        public void Throws_when_path_is_blank()
        {
            Assert.Throws<ArgumentException>(() => new JsonAuditReader().Read(""));
        }

        [Fact]
        public void Throws_when_file_does_not_exist()
        {
            Assert.Throws<FileNotFoundException>(() =>
                new JsonAuditReader().Read(@"C:\does\not\exist\file.json"));
        }

        [Fact]
        public void Round_trip_minimal_snapshot()
        {
            var input = new AuditSnapshot
            {
                ForestName = "exemple.local",
                GeneratedAt = new DateTime(2026, 5, 23, 14, 0, 0, DateTimeKind.Utc),
                DomainControllers = new List<DomainControllerInfo>()
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Equal("exemple.local", output.ForestName);
            Assert.Equal(input.GeneratedAt.ToUniversalTime(), output.GeneratedAt.ToUniversalTime());
        }

        [Fact]
        public void Round_trip_preserves_domain_controllers()
        {
            var input = new AuditSnapshot
            {
                ForestName = "exemple.local",
                DomainControllers = new[]
                {
                    new DomainControllerInfo
                    {
                        HostName = "DC01.exemple.local",
                        Domain = "exemple.local",
                        Forest = "exemple.local",
                        SiteName = "Paris",
                        IPAddress = "10.0.0.1",
                        OSVersion = "Windows Server 2022",
                        IsGlobalCatalog = true,
                        IsReadOnly = false,
                        Roles = new[] { "PDC", "RID" }
                    }
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Single(output.DomainControllers);
            var dc = output.DomainControllers[0];
            Assert.Equal("DC01.exemple.local", dc.HostName);
            Assert.Equal("Paris", dc.SiteName);
            Assert.True(dc.IsGlobalCatalog);
            Assert.False(dc.IsReadOnly);
            Assert.NotNull(dc.Roles);
            Assert.Contains("PDC", dc.Roles);
        }

        [Fact]
        public void Round_trip_preserves_enum_values()
        {
            var input = new AuditSnapshot
            {
                ReplicationLinks = new[]
                {
                    new ReplicationLink
                    {
                        DestinationDc = "DC02",
                        SourceDc = "DC01",
                        NamingContext = "DC=exemple,DC=local",
                        Status = ReplicationLinkStatus.Failing,
                        LastResultCode = 8524
                    }
                },
                HealthScore = new HealthScore
                {
                    Value = 75,
                    Level = HealthLevel.Warning,
                    ReplicationScore = 70
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Equal(ReplicationLinkStatus.Failing, output.ReplicationLinks[0].Status);
            Assert.Equal(HealthLevel.Warning, output.HealthScore.Level);
        }

        [Fact]
        public void Round_trip_preserves_nullable_subscores()
        {
            var input = new AuditSnapshot
            {
                HealthScore = new HealthScore
                {
                    Value = 80, Level = HealthLevel.Warning,
                    ReplicationScore = 80,
                    DnsScore = null,  // sonde inactive
                    PortScore = 60
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Null(output.HealthScore.DnsScore);
            Assert.Equal(60, output.HealthScore.PortScore);
        }

        [Fact]
        public void Round_trip_preserves_dns_health()
        {
            var input = new AuditSnapshot
            {
                DnsHealth = new DnsHealthResult
                {
                    Checks = new[]
                    {
                        new DnsCheckResult
                        {
                            RecordName = "_ldap._tcp.dc._msdcs.exemple.local",
                            Type = DnsCheckedRecordType.SrvLdap,
                            Status = DnsCheckStatus.Ok,
                            Target = "dc01.exemple.local",
                            Port = 389
                        },
                        new DnsCheckResult
                        {
                            RecordName = "_kerberos._tcp.exemple.local",
                            Type = DnsCheckedRecordType.SrvKerberos,
                            Status = DnsCheckStatus.Missing,
                            ErrorCode = 9501
                        }
                    }
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.NotNull(output.DnsHealth);
            Assert.Equal(2, output.DnsHealth.Checks.Count);
            Assert.Equal(DnsCheckStatus.Ok, output.DnsHealth.Checks[0].Status);
            Assert.Equal(389, output.DnsHealth.Checks[0].Port);
            Assert.Equal(DnsCheckStatus.Missing, output.DnsHealth.Checks[1].Status);
        }

        [Fact]
        public void Round_trip_preserves_port_health()
        {
            var input = new AuditSnapshot
            {
                PortHealth = new PortHealthResult
                {
                    Checks = new[]
                    {
                        new PortCheckResult
                        {
                            HostName = "dc01.exemple.local",
                            Port = 389,
                            ServiceLabel = "LDAP",
                            Status = PortCheckStatus.Open,
                            ResponseTime = TimeSpan.FromMilliseconds(120)
                        }
                    }
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Single(output.PortHealth.Checks);
            Assert.Equal(389, output.PortHealth.Checks[0].Port);
            Assert.Equal(PortCheckStatus.Open, output.PortHealth.Checks[0].Status);
            Assert.Equal(TimeSpan.FromMilliseconds(120), output.PortHealth.Checks[0].ResponseTime);
        }

        [Fact]
        public void Round_trip_preserves_issues()
        {
            var input = new AuditSnapshot
            {
                Issues = new[]
                {
                    new DetectedIssue
                    {
                        Code = "DC_ISOLATED",
                        Severity = IssueSeverity.Critical,
                        Title = "DC isole",
                        Description = "Description",
                        AffectedItems = new[] { "DC02", "DC03" }
                    }
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Single(output.Issues);
            Assert.Equal("DC_ISOLATED", output.Issues[0].Code);
            Assert.Equal(IssueSeverity.Critical, output.Issues[0].Severity);
            Assert.Equal(2, output.Issues[0].AffectedItems.Count);
        }

        [Fact]
        public void Round_trip_preserves_site_link_timespan()
        {
            var input = new AuditSnapshot
            {
                SiteLinks = new[]
                {
                    new SiteLinkInfo
                    {
                        Name = "Paris-Lyon",
                        Cost = 100,
                        ReplicationInterval = TimeSpan.FromMinutes(180),
                        ChangeNotificationEnabled = true,
                        TransportType = "IP",
                        Sites = new[] { "Paris", "Lyon" }
                    }
                }
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Single(output.SiteLinks);
            Assert.Equal(TimeSpan.FromMinutes(180), output.SiteLinks[0].ReplicationInterval);
            Assert.True(output.SiteLinks[0].ChangeNotificationEnabled);
        }

        [Fact]
        public void Null_dns_health_remains_null_after_roundtrip()
        {
            var input = new AuditSnapshot
            {
                ForestName = "exemple.local",
                DnsHealth = null,
                PortHealth = null
            };

            var output = RoundTrip(input, _tempFile);

            Assert.Null(output.DnsHealth);
            Assert.Null(output.PortHealth);
        }

        [Fact]
        public void Unknown_property_in_json_is_silently_ignored()
        {
            // Forward compatibility : un JSON produit par une version plus récente
            // contenant un champ que cette version ne connait pas doit se charger
            // partiellement plutot que d'echouer.
            var json = @"{
                ""forestName"": ""exemple.local"",
                ""futureField"": ""ignored"",
                ""domainControllers"": []
            }";

            var snapshot = new JsonAuditReader().ReadFromString(json);

            Assert.Equal("exemple.local", snapshot.ForestName);
            Assert.NotNull(snapshot.DomainControllers);
        }

        [Fact]
        public void Empty_json_string_throws()
        {
            Assert.Throws<ArgumentException>(() => new JsonAuditReader().ReadFromString(""));
        }
    }
}
