using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Models;

namespace ADReplic.Core.Export
{
    /// <summary>
    /// Exporte deux fichiers CSV : "{base}.dc.csv" et "{base}.replication.csv".
    /// Encodage UTF-8 avec BOM pour ouverture correcte sous Excel.
    /// Séparateur point-virgule (locale FR par défaut sous Windows).
    /// </summary>
    public sealed class CsvAuditExporter : IAuditExporter
    {
        private const char Separator = ';';

        public string Format => "CSV";
        public string DefaultFileExtension => ".csv";

        public void Export(AuditSnapshot snapshot, string targetPath)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("Chemin requis.", nameof(targetPath));

            var basePath = StripExtension(targetPath);
            WriteDomainControllers(snapshot.DomainControllers, basePath + ".dc.csv");
            WriteReplicationLinks(snapshot.ReplicationLinks, basePath + ".replication.csv");
            WriteReplicationFailures(snapshot.ReplicationFailures, basePath + ".failures.csv");
            WriteSites(snapshot.Sites, basePath + ".sites.csv");
            WriteSiteLinks(snapshot.SiteLinks, basePath + ".sitelinks.csv");
        }

        private static void WriteReplicationFailures(IReadOnlyList<ReplicationFailureInfo> failures, string path)
        {
            var sb = new StringBuilder();
            AppendHeader(sb,
                "DestinationDc", "SourceDc", "FirstFailureTime", "DurationMinutes",
                "ConsecutiveFailureCount", "LastErrorCode", "LastErrorMessage", "Severity");
            if (failures != null)
            {
                foreach (var f in failures)
                {
                    AppendRow(sb,
                        f.DestinationDc,
                        f.SourceDc,
                        FormatDate(f.FirstFailureTime),
                        ((int)f.FailureDuration.TotalMinutes).ToString(CultureInfo.InvariantCulture),
                        f.ConsecutiveFailureCount.ToString(CultureInfo.InvariantCulture),
                        f.LastErrorCode.ToString(CultureInfo.InvariantCulture),
                        f.LastErrorMessage,
                        f.Severity.ToString());
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void WriteSites(IReadOnlyList<SiteInfo> sites, string path)
        {
            var sb = new StringBuilder();
            AppendHeader(sb, "Name", "Location", "DomainControllers", "Subnets", "BridgeheadServers");
            if (sites != null)
            {
                foreach (var s in sites)
                {
                    AppendRow(sb,
                        s.Name,
                        s.Location,
                        s.DomainControllers == null ? "" : string.Join("|", s.DomainControllers),
                        s.Subnets == null ? "" : string.Join("|", s.Subnets),
                        s.BridgeheadServers == null ? "" : string.Join("|", s.BridgeheadServers));
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void WriteSiteLinks(IReadOnlyList<SiteLinkInfo> links, string path)
        {
            var sb = new StringBuilder();
            AppendHeader(sb, "Name", "Sites", "Cost", "ReplicationIntervalMinutes", "TransportType", "ChangeNotificationEnabled");
            if (links != null)
            {
                foreach (var l in links)
                {
                    AppendRow(sb,
                        l.Name,
                        l.Sites == null ? "" : string.Join("|", l.Sites),
                        l.Cost.ToString(CultureInfo.InvariantCulture),
                        ((int)l.ReplicationInterval.TotalMinutes).ToString(CultureInfo.InvariantCulture),
                        l.TransportType,
                        l.ChangeNotificationEnabled ? "1" : "0");
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void WriteDomainControllers(IReadOnlyList<DomainControllerInfo> dcs, string path)
        {
            var sb = new StringBuilder();
            AppendHeader(sb, "HostName", "Domain", "Forest", "Site", "IPAddress", "OSVersion", "IsGC", "IsRODC", "Roles");
            if (dcs != null)
            {
                foreach (var dc in dcs)
                {
                    AppendRow(sb,
                        dc.HostName,
                        dc.Domain,
                        dc.Forest,
                        dc.SiteName,
                        dc.IPAddress,
                        dc.OSVersion,
                        dc.IsGlobalCatalog ? "1" : "0",
                        dc.IsReadOnly ? "1" : "0",
                        dc.Roles == null ? "" : string.Join("|", dc.Roles));
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void WriteReplicationLinks(IReadOnlyList<ReplicationLink> links, string path)
        {
            var sb = new StringBuilder();
            AppendHeader(sb,
                "DestinationDc", "SourceDc", "NamingContext", "PartitionType", "IsIntraSite",
                "LastAttempt", "LastSuccess", "LatencyMinutes", "ConsecutiveFailures",
                "LastResultCode", "LastResultMessage", "Status");
            if (links != null)
            {
                foreach (var l in links)
                {
                    AppendRow(sb,
                        l.DestinationDc,
                        l.SourceDc,
                        l.NamingContext,
                        l.PartitionType,
                        l.IsIntraSite ? "1" : "0",
                        FormatDate(l.LastAttempt),
                        FormatDate(l.LastSuccess),
                        l.Latency.HasValue ? l.Latency.Value.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture) : "",
                        l.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture),
                        l.LastResultCode.ToString(CultureInfo.InvariantCulture),
                        l.LastResultMessage,
                        l.Status.ToString());
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void AppendHeader(StringBuilder sb, params string[] cols)
        {
            sb.AppendLine(string.Join(Separator.ToString(), cols));
        }

        private static void AppendRow(StringBuilder sb, params string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(EscapeCell(cells[i]));
            }
            sb.AppendLine();
        }

        private static string EscapeCell(string value)
        {
            if (value == null) return "";
            // RFC 4180 simplifié : guillemets si la cellule contient séparateur, guillemet ou saut de ligne.
            var needsQuoting = value.IndexOfAny(new[] { Separator, '"', '\n', '\r' }) >= 0;
            if (!needsQuoting) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : "";
        }

        private static string StripExtension(string path)
        {
            var ext = Path.GetExtension(path);
            return string.IsNullOrEmpty(ext) ? path : path.Substring(0, path.Length - ext.Length);
        }
    }
}
