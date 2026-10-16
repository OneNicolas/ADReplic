using System;
using System.IO;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
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
                "exemple.local",
                new[]
                {
                    new DomainControllerInfo
                    {
                        HostName = "dc01.exemple.local",
                        Domain = "exemple.local",
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
                        NamingContext = "DC=exemple,DC=local",
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
                        RecordName = "_ldap._tcp.dc._msdcs.exemple.local",
                        Type = DnsCheckedRecordType.SrvLdap,
                        Status = DnsCheckStatus.Ok,
                        Target = "dc01.exemple.local",
                        Port = 389,
                        Priority = 0,
                        Weight = 100
                    },
                    new DnsCheckResult
                    {
                        RecordName = "_kpasswd._tcp.exemple.local",
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
                        HostName = "dc01.exemple.local", Port = 389, ServiceLabel = "LDAP",
                        Status = PortCheckStatus.Open, ResponseTime = System.TimeSpan.FromMilliseconds(12)
                    },
                    new PortCheckResult
                    {
                        HostName = "dc01.exemple.local", Port = 636, ServiceLabel = "LDAPS",
                        Status = PortCheckStatus.Closed, ResponseTime = System.TimeSpan.FromMilliseconds(5)
                    }
                }
            };

            return AuditSnapshotBuilder.Build(
                "exemple.local",
                new[] { new DomainControllerInfo { HostName = "dc01.exemple.local", Domain = "exemple.local" } },
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
            Assert.Contains("exemple.local", content);
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
        public void Html_export_marks_unreachable_dc()
        {
            var snapshot = BuildSampleSnapshot();
            snapshot.DomainControllers[0].IsReachable = false;
            var target = Path.Combine(_tempDir, "report.html");

            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("(injoignable)", content);
        }

        [Fact]
        public void Html_export_does_not_mark_reachable_dc()
        {
            var target = Path.Combine(_tempDir, "report.html");

            new HtmlAuditExporter().Export(BuildSampleSnapshot(), target);

            var content = File.ReadAllText(target);
            Assert.DoesNotContain("(injoignable)", content);
        }

        [Fact]
        public void Html_export_omits_clock_section_without_probe()
        {
            var target = Path.Combine(_tempDir, "report.html");

            new HtmlAuditExporter().Export(BuildSampleSnapshot(), target);

            Assert.DoesNotContain("<h2>Horloges</h2>", File.ReadAllText(target));
        }

        [Fact]
        public void Html_export_writes_clock_section_with_reference_and_offsets()
        {
            // dc01 est deja maitre PDC dans le snapshot d'exemple : il sert de reference.
            var snapshot = BuildSampleSnapshot();
            var observed = new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);
            snapshot.ClockHealth = new ClockHealthResult
            {
                ProbedAtUtc = observed,
                Checks = new[]
                {
                    new ClockCheckResult { HostName = "dc01.exemple.local", Status = ClockCheckStatus.Ok, DcTimeUtc = observed, ObservedAtUtc = observed, OffsetFromAuditHost = TimeSpan.Zero },
                    new ClockCheckResult { HostName = "dc02.exemple.local", Status = ClockCheckStatus.Ok, DcTimeUtc = observed.AddMinutes(10), ObservedAtUtc = observed, OffsetFromAuditHost = TimeSpan.FromMinutes(10) }
                }
            };
            var target = Path.Combine(_tempDir, "report.html");

            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("<h2>Horloges</h2>", content);
            Assert.Contains("maître PDC", content);
            Assert.Contains("+10 min", content);
            Assert.Contains("réf.", content);
        }

        [Fact]
        public void Html_export_clock_section_explains_skipped_dc()
        {
            var snapshot = BuildSampleSnapshot();
            snapshot.ClockHealth = new ClockHealthResult
            {
                Checks = new[]
                {
                    new ClockCheckResult { HostName = "dc01.exemple.local", Status = ClockCheckStatus.Ok, OffsetFromAuditHost = TimeSpan.Zero },
                    new ClockCheckResult { HostName = "dc02.exemple.local", Status = ClockCheckStatus.Skipped }
                }
            };
            var target = Path.Combine(_tempDir, "report.html");

            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("Non interrogée", content);
            Assert.Contains("\u2014", content);
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
            Assert.Contains("exemple.local", content);
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
            Assert.Contains("_ldap._tcp.dc._msdcs.exemple.local", content);
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
        public void Json_export_excludes_computed_error_explanation()
        {
            // La fiche explicative est recalculée depuis le catalogue à la lecture :
            // la sérialiser gonflerait chaque audit et figerait un texte susceptible d'évoluer.
            var snapshot = BuildSampleSnapshot();
            snapshot.ReplicationFailures = new[]
            {
                new ReplicationFailureInfo
                {
                    DestinationDc = "DC01", SourceDc = "DC02",
                    FirstFailureTime = System.DateTime.Now.AddHours(-2),
                    ConsecutiveFailureCount = 3, LastErrorCode = 1722,
                    LastErrorMessage = "Le serveur RPC n'est pas disponible."
                }
            };
            var target = Path.Combine(_tempDir, "audit.json");
            new JsonAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.DoesNotContain("\"explanation\"", content);
            Assert.DoesNotContain("\"errorTitle\"", content);
            Assert.DoesNotContain("probableCauses", content);
            // Le reste de l'echec reste bien exporte.
            Assert.Contains("\"lastErrorCode\"", content);
        }

        [Fact]
        public void Html_export_includes_plain_summary_and_remediation()
        {
            var snapshot = BuildSampleSnapshot();
            snapshot.ReplicationFailures = new[]
            {
                new ReplicationFailureInfo
                {
                    DestinationDc = "DC01", SourceDc = "DC02",
                    FirstFailureTime = System.DateTime.Now.AddDays(-3),
                    ConsecutiveFailureCount = 3, LastErrorCode = 1722
                }
            };
            var target = Path.Combine(_tempDir, "audit.html");
            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("plain-summary", content);
            Assert.Contains("1 échec de réplication", content);
            Assert.Contains("Serveur RPC injoignable", content);
            Assert.Contains("Comment corriger", content);
            Assert.Contains("<details", content);
            Assert.Contains("learn.microsoft.com", content);
        }

        [Fact]
        public void Html_export_shows_issue_impact_and_recommendation_columns()
        {
            // Un DC sous OS obsolete declenche OS_UNSUPPORTED via le builder.
            var snapshot = BuildSampleSnapshot();
            var dc = new DomainControllerInfo { HostName = "dc-old.exemple.local", Domain = "exemple.local", OSVersion = "Windows Server 2008 R2" };
            snapshot = AuditSnapshotBuilder.Build("exemple.local", new[] { dc }, new ReplicationLink[0]);
            var target = Path.Combine(_tempDir, "audit.html");
            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("Pourquoi c'est important", content);
            Assert.Contains("Que faire", content);
            Assert.Contains("<ol class=\"steps\">", content);
        }

        [Fact]
        public void Html_export_folds_consequences_of_an_unreachable_dc_under_their_parent()
        {
            var snapshot = BuildSampleSnapshot();
            snapshot.Issues = new[]
            {
                new DetectedIssue { Code = "DC_UNREACHABLE", Severity = IssueSeverity.Critical, Title = "DC injoignable : dc02.exemple.local", RelatedHost = "dc02.exemple.local" },
                new DetectedIssue { Code = "PORT_CRITICAL_CLOSED", Severity = IssueSeverity.Critical, Title = "Port 389 ferme", RelatedHost = "dc02.exemple.local" },
                new DetectedIssue { Code = "PORT_CLOSED_OR_FILTERED", Severity = IssueSeverity.Warning, Title = "Port 636 ferme", RelatedHost = "dc02.exemple.local" },
                new DetectedIssue { Code = "SITE_NO_SUBNET", Severity = IssueSeverity.Warning, Title = "Site sans subnet" }
            };
            var target = Path.Combine(_tempDir, "audit.html");
            new HtmlAuditExporter().Export(snapshot, target);

            var content = File.ReadAllText(target);
            Assert.Contains("2 anomalies liées à l'indisponibilité de dc02.exemple.local", content);

            var detailsStart = content.IndexOf("<details class=\"derived\"", StringComparison.Ordinal);
            var detailsEnd   = content.IndexOf("</details>", detailsStart, StringComparison.Ordinal);
            Assert.True(detailsStart > 0);
            Assert.InRange(content.IndexOf("PORT_CRITICAL_CLOSED", StringComparison.Ordinal), detailsStart, detailsEnd);
            Assert.InRange(content.IndexOf("PORT_CLOSED_OR_FILTERED", StringComparison.Ordinal), detailsStart, detailsEnd);
            // L'anomalie sans lien avec le DC injoignable reste une ligne de premier niveau.
            Assert.False(content.IndexOf("SITE_NO_SUBNET", StringComparison.Ordinal) > detailsStart
                      && content.IndexOf("SITE_NO_SUBNET", StringComparison.Ordinal) < detailsEnd);
        }

        [Fact]
        public void Csv_issues_and_failures_files_have_readability_columns_appended()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            var issuesHeader = File.ReadLines(basePath + ".issues.csv").First();
            Assert.EndsWith("Impact;Recommendation;DocumentationUrl", issuesHeader.Replace(",", ";"));

            var failuresHeader = File.ReadLines(basePath + ".failures.csv").First();
            Assert.EndsWith("ErrorTitle;DocumentationUrl", failuresHeader.Replace(",", ";"));
        }

        [Fact]
        public void Csv_dc_file_contains_header_and_data()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            var content = File.ReadAllText(basePath + ".dc.csv");
            Assert.Contains("HostName", content);
            Assert.Contains("dc01.exemple.local", content);
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
            Assert.Contains("_ldap._tcp.dc._msdcs.exemple.local", content);
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
