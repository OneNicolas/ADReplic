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
        public void Csv_export_produces_six_files_including_issues()
        {
            var basePath = Path.Combine(_tempDir, "audit");
            new CsvAuditExporter().Export(BuildSampleSnapshot(), basePath);

            Assert.True(File.Exists(basePath + ".dc.csv"));
            Assert.True(File.Exists(basePath + ".replication.csv"));
            Assert.True(File.Exists(basePath + ".failures.csv"));
            Assert.True(File.Exists(basePath + ".sites.csv"));
            Assert.True(File.Exists(basePath + ".sitelinks.csv"));
            Assert.True(File.Exists(basePath + ".issues.csv"));
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
    }
}
