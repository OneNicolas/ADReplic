using System;
using System.IO;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Export
{
    /// <summary>
    /// Tests d'intégration légers des exporters : on vérifie qu'un fichier valide
    /// est produit et qu'il contient les données clés. On ne teste pas la mise en
    /// page exacte qui changera à chaque ajustement visuel.
    /// </summary>
    public class ExportersTests : IDisposable
    {
        private readonly string _tempDir;

        public ExportersTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ADReplic.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* meilleur effort */ }
        }

        private static AuditSnapshot BuildSampleSnapshot()
        {
            return AuditSnapshotBuilder.Build(
                "scopi.local",
                new[]
                {
                    new DomainControllerInfo
                    {
                        HostName = "DC01.scopi.local",
                        Domain = "scopi.local",
                        SiteName = "Paris",
                        IPAddress = "10.0.0.1",
                        OSVersion = "Windows Server 2022",
                        IsGlobalCatalog = true,
                        Roles = new[] { "PDC", "RID" }
                    }
                },
                new[]
                {
                    new ReplicationLink
                    {
                        DestinationDc = "DC01",
                        SourceDc = "DC02",
                        NamingContext = "DC=scopi,DC=local",
                        PartitionType = "Domaine",
                        Status = ReplicationLinkStatus.Healthy,
                        ConsecutiveFailures = 0,
                        LastResultCode = 0
                    }
                });
        }

        /// <summary>Snapshot enrichi avec des sondes DNS et Ports pour tester les nouveaux exports.</summary>
        private static AuditSnapshot BuildSampleSnapshotWithHealthProbes()
        {
            var dns = new DnsHealthResult
            {
                Checks = new[]
                {
                    new DnsCheckResult
                    {
                        RecordName = "_ldap._tcp.dc._msdcs.scopi.local",
                        Type = DnsCheckedRecordType.SrvLdap,
                        Status = DnsCheckStatus.Ok,
                        Target = "DC01.scopi.local",
                        Port = 389,
                        Priority = 0,
                        Weight = 100
                    },
                    new DnsCheckResult
                    {
                        RecordName = "_kpasswd._tcp.scopi.local",
                        Type = DnsCheckedRecordType.SrvKpasswd,
                        Status = DnsCheckStatus.Missing,
                        ErrorCode = 9003,
                        ErrorMessage = "DNS name does not exist."
                    }
                }
            };

            var ports = new PortHealthResult
            {
                Checks = new[]
                {
                    new PortCheckResult
                    {
                        HostName = "DC01.scopi.local", Port = 389, ServiceLabel = "LDAP",
                        Status = PortCheckStatus.Open, ResponseTime = System.TimeSpan.FromMilliseconds(12)
                    },
                    new PortCheckResult
                    {
                        HostName = "DC01.scopi.local", Port = 636, ServiceLabel = "LDAPS",
                        Status = PortCheckStatus.Closed, ResponseTime = System.TimeSpan.FromMilliseconds(5)
                    }
                }
            };

            return AuditSnapshotBuilder.Build(
                "scopi.local",
                new[] { new DomainControllerInfo { HostName = "DC01.scopi.local", Domain = "scopi.local" } },
                System.Array.Empty<ReplicationLink>(),
                topology: null,
                failures: null,
                isSingleDcMode: false,
                dnsHealth: dns,
                portHealth: ports);
        }

        [Fact]
        public void Html_export_writes_valid_file_containing_forest_name()
        {
            var target = Path.Combine(_tempDir, "report.html");
            var exporter = new HtmlAuditExporter();

            exporter.Export(BuildSampleSnapshot(), target);

            Assert.True(File.Exists(target));
            var content = File.ReadAllText(target);
            Assert.Contains("scopi.local", content);
            Assert.Contains("DC01", content);
            Assert.Contains("<html", content);
        }

        [Fact]
        public void Html_export_shows_reassuring_banner_when_no_failures()
        {
            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(BuildSampleSnapshot(), target);

            var content = File.ReadAllText(target);
            Assert.Contains("Aucun échec", content);
        }

        [Fact]
        public void Json_export_writes_parseable_json()
        {
            var target = Path.Combine(_tempDir, "audit.json");
            new JsonAuditExporter().Export(BuildSampleSnapshot(), target);

            Assert.True(File.Exists(target));
            var content = File.ReadAllText(target);

            // On ne dépend pas d'un parseur JSON externe : on vérifie les marqueurs structurels.
            Assert.StartsWith("{", content.TrimStart());
            Assert.EndsWith("}", content.TrimEnd());
            Assert.Contains("\"forestName\"", content);
            Assert.Contains("scopi.local", content);
        }

        [Fact]
        public void Csv_export_produces_eight_files_one_per_category()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            Assert.True(File.Exists(basePath + ".dc.csv"));
            Assert.True(File.Exists(basePath + ".replication.csv"));
            Assert.True(File.Exists(basePath + ".failures.csv"));
            Assert.True(File.Exists(basePath + ".sites.csv"));
            Assert.True(File.Exists(basePath + ".sitelinks.csv"));
            Assert.True(File.Exists(basePath + ".issues.csv"));
            Assert.True(File.Exists(basePath + ".dns.csv"));
            Assert.True(File.Exists(basePath + ".ports.csv"));
        }

        [Fact]
        public void Csv_issues_file_contains_expected_header()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            var content = File.ReadAllText(basePath + ".issues.csv");
            Assert.Contains("Severity", content);
            Assert.Contains("Code", content);
            Assert.Contains("AffectedItems", content);
        }

        [Fact]
        public void Html_export_includes_diagnostics_section()
        {
            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(BuildSampleSnapshot(), target);

            var content = File.ReadAllText(target);
            Assert.Contains("Diagnostics", content);
        }

        [Fact]
        public void Html_export_omits_dns_section_when_probe_not_executed()
        {
            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(BuildSampleSnapshot(), target);

            var content = File.ReadAllText(target);
            // Sans sonde DNS, pas de section : l'app actuelle continue d'afficher
            // le rapport historique sans bandeau vide.
            Assert.DoesNotContain("Santé DNS", content);
        }

        [Fact]
        public void Html_export_omits_port_section_when_probe_not_executed()
        {
            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(BuildSampleSnapshot(), target);

            var content = File.ReadAllText(target);
            Assert.DoesNotContain("Santé réseau", content);
        }

        [Fact]
        public void Html_export_includes_dns_section_with_probe_data()
        {
            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(BuildSampleSnapshotWithHealthProbes(), target);

            var content = File.ReadAllText(target);
            Assert.Contains("Santé DNS", content);
            Assert.Contains("_ldap._tcp.dc._msdcs.scopi.local", content);
            Assert.Contains("SRV _kpasswd", content);
            Assert.Contains("Missing", content);
        }

        [Fact]
        public void Html_export_includes_port_section_with_probe_data()
        {
            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(BuildSampleSnapshotWithHealthProbes(), target);

            var content = File.ReadAllText(target);
            Assert.Contains("Santé réseau", content);
            Assert.Contains("LDAPS", content);
            Assert.Contains("Closed", content);
            Assert.Contains("Open", content);
        }

        [Fact]
        public void Html_export_shows_reassuring_banner_when_dns_all_ok()
        {
            var dns = new DnsHealthResult
            {
                Checks = new[]
                {
                    new DnsCheckResult
                    {
                        RecordName = "_ldap._tcp.dc._msdcs.exemple.local",
                        Type = DnsCheckedRecordType.SrvLdap,
                        Status = DnsCheckStatus.Ok,
                        Target = "dc01.exemple.local", Port = 389
                    }
                }
            };
            var snapshot = AuditSnapshotBuilder.Build(
                "exemple.local", null, null, null, null, false, dns, null);

            var target = Path.Combine(_tempDir, "report.html");
            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("Tous les enregistrements DNS testés sont résolvables", content);
        }

        [Fact]
        public void Json_export_includes_issues_property()
        {
            var target = Path.Combine(_tempDir, "audit.json");
            new JsonAuditExporter().Export(BuildSampleSnapshot(), target);

            var content = File.ReadAllText(target);
            Assert.Contains("\"issues\"", content);
        }

        [Fact]
        public void Csv_dc_file_contains_header_and_data()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            var content = File.ReadAllText(basePath + ".dc.csv");
            Assert.Contains("HostName", content);
            Assert.Contains("DC01.scopi.local", content);
            Assert.Contains("Paris", content);
        }

        [Fact]
        public void Csv_dns_file_has_header_even_when_no_probe_executed()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            var content = File.ReadAllText(basePath + ".dns.csv");
            Assert.Contains("RecordName", content);
            Assert.Contains("Status", content);
        }

        [Fact]
        public void Csv_ports_file_has_header_even_when_no_probe_executed()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            var content = File.ReadAllText(basePath + ".ports.csv");
            Assert.Contains("HostName", content);
            Assert.Contains("Port", content);
            Assert.Contains("ServiceLabel", content);
        }

        [Fact]
        public void Csv_dns_file_contains_probe_data_when_provided()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshotWithHealthProbes(), basePath);

            var content = File.ReadAllText(basePath + ".dns.csv");
            Assert.Contains("_ldap._tcp.dc._msdcs.scopi.local", content);
            Assert.Contains("SrvLdap", content);
            Assert.Contains("Ok", content);
            Assert.Contains("SrvKpasswd", content);
            Assert.Contains("Missing", content);
        }

        [Fact]
        public void Csv_ports_file_contains_probe_data_when_provided()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshotWithHealthProbes(), basePath);

            var content = File.ReadAllText(basePath + ".ports.csv");
            Assert.Contains("LDAP", content);
            Assert.Contains("LDAPS", content);
            Assert.Contains("389", content);
            Assert.Contains("636", content);
            Assert.Contains("Open", content);
            Assert.Contains("Closed", content);
        }
    }
}
