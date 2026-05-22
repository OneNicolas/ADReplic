using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Export
{
    /// <summary>
    /// Exporte un rapport HTML autonome (CSS inline, aucune dépendance externe).
    /// Conçu pour être lisible hors-ligne et joignable par mail à un client.
    /// </summary>
    public sealed class HtmlAuditExporter : IAuditExporter
    {
        public string Format => "HTML";
        public string DefaultFileExtension => ".html";

        public void Export(AuditSnapshot snapshot, string targetPath)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("Chemin requis.", nameof(targetPath));

            var sb = new StringBuilder(16384);
            AppendDocument(sb, snapshot);
            File.WriteAllText(targetPath, sb.ToString(), new UTF8Encoding(true));
        }

        private static void AppendDocument(StringBuilder sb, AuditSnapshot s)
        {
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"fr\"><head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendFormat("<title>Audit AD — {0}</title>", Encode(s.ForestName)).AppendLine();
            sb.AppendLine(InlineStyles());
            sb.AppendLine("</head><body>");

            AppendHeader(sb, s);
            AppendSummaryCards(sb, s.Summary);
            AppendDiagnosticsSection(sb, s.Issues);
            AppendDnsHealthSection(sb, s.DnsHealth);
            AppendPortHealthSection(sb, s.PortHealth);
            AppendFailuresSection(sb, s.ReplicationFailures);
            AppendReplicationTable(sb, s.ReplicationLinks);
            AppendSitesSection(sb, s.Sites);
            AppendSiteLinksTable(sb, s.SiteLinks);
            AppendDcTable(sb, s.DomainControllers);
            AppendFooter(sb);

            sb.AppendLine("</body></html>");
        }

        private static void AppendHeader(StringBuilder sb, AuditSnapshot s)
        {
            sb.AppendLine("<header class=\"hdr\">");
            sb.AppendLine("  <h1>Audit de réplication Active Directory</h1>");
            sb.AppendFormat("  <p class=\"meta\">Forêt : <strong>{0}</strong> &nbsp;·&nbsp; Généré le {1} par {2}@{3}</p>",
                Encode(s.ForestName),
                s.GeneratedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR")),
                Encode(s.GeneratedBy),
                Encode(s.GeneratedOn)).AppendLine();
            AppendHealthBadge(sb, s.HealthScore);
            sb.AppendLine("</header>");
        }

        private static void AppendHealthBadge(StringBuilder sb, HealthScore score)
        {
            if (score == null) return;
            var cls = score.Level == HealthLevel.Excellent ? "ok"
                    : score.Level == HealthLevel.Warning   ? "warn"
                    : "fail";
            sb.AppendFormat("  <div class=\"health-badge {0}\">", cls).AppendLine();
            sb.AppendFormat("    <div class=\"hb-score\">{0}<span>/100</span></div>", score.Value).AppendLine();
            sb.AppendFormat("    <div class=\"hb-meta\"><div class=\"hb-label\">{0}</div><div class=\"hb-summary\">{1}</div></div>",
                Encode(score.Label), Encode(score.Summary)).AppendLine();
            sb.AppendLine("  </div>");
        }

        private static void AppendSummaryCards(StringBuilder sb, AuditSummary sum)
        {
            if (sum == null) return;
            sb.AppendLine("<section class=\"cards\">");
            AppendCard(sb, "Contrôleurs", sum.DomainControllerCount, "neutral");
            AppendCard(sb, "Liens de réplication", sum.ReplicationLinkCount, "neutral");
            AppendCard(sb, "Sains", sum.HealthyLinks, "ok");
            AppendCard(sb, "Avertissements", sum.WarningLinks, "warn");
            AppendCard(sb, "En échec", sum.FailingLinks, "fail");
            if (sum.UnreachableDcs > 0)
                AppendCard(sb, "DC injoignables", sum.UnreachableDcs, "fail");
            sb.AppendLine("</section>");
        }

        private static void AppendCard(StringBuilder sb, string label, int value, string cls)
        {
            sb.AppendFormat("  <div class=\"card {0}\"><div class=\"v\">{1}</div><div class=\"l\">{2}</div></div>",
                cls, value, Encode(label)).AppendLine();
        }

        private static void AppendReplicationTable(StringBuilder sb, IReadOnlyList<ReplicationLink> links)
        {
            sb.AppendLine("<section><h2>Matrice de réplication</h2>");
            if (links == null || links.Count == 0)
            {
                sb.AppendLine("<p class=\"empty\">Aucune donnée de réplication.</p></section>");
                return;
            }

            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th></th><th>Destination</th><th>Source</th><th>Contexte de nommage</th><th>Type</th>");
            sb.AppendLine("<th>Dernier succès</th><th>Dernière tentative</th><th>Latence</th><th>Échecs</th><th>Message</th>");
            sb.AppendLine("</tr></thead><tbody>");

            // On groupe les échecs en haut pour mise en évidence immédiate.
            var ordered = links
                .OrderBy(l => StatusWeight(l.Status))
                .ThenBy(l => l.DestinationDc)
                .ThenBy(l => l.SourceDc);

            foreach (var l in ordered)
            {
                var dot = StatusDotClass(l.Status);
                sb.Append("<tr>");
                sb.AppendFormat("<td><span class=\"dot {0}\"></span></td>", dot);
                sb.AppendFormat("<td>{0}</td>", Encode(l.DestinationDc));
                sb.AppendFormat("<td>{0}</td>", Encode(l.SourceDc));
                sb.AppendFormat("<td class=\"mono\">{0}</td>", Encode(l.NamingContext));
                sb.AppendFormat("<td>{0}</td>", Encode(l.PartitionType));
                sb.AppendFormat("<td>{0}</td>", FormatDate(l.LastSuccess));
                sb.AppendFormat("<td>{0}</td>", FormatDate(l.LastAttempt));
                sb.AppendFormat("<td>{0}</td>", FormatLatency(l.Latency));
                sb.AppendFormat("<td class=\"num\">{0}</td>", l.ConsecutiveFailures);
                sb.AppendFormat("<td>{0}</td>", Encode(l.LastResultMessage));
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table></section>");
        }

        private static void AppendDcTable(StringBuilder sb, IReadOnlyList<DomainControllerInfo> dcs)
        {
            sb.AppendLine("<section><h2>Contrôleurs de domaine</h2>");
            if (dcs == null || dcs.Count == 0)
            {
                sb.AppendLine("<p class=\"empty\">Aucun contrôleur découvert.</p></section>");
                return;
            }
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>Hôte</th><th>Domaine</th><th>Site</th><th>IP</th><th>OS</th><th>GC</th><th>Rôles FSMO</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var dc in dcs.OrderBy(d => d.Domain).ThenBy(d => d.SiteName).ThenBy(d => d.HostName))
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td>{0}</td>", Encode(dc.HostName));
                sb.AppendFormat("<td>{0}</td>", Encode(dc.Domain));
                sb.AppendFormat("<td>{0}</td>", Encode(dc.SiteName));
                sb.AppendFormat("<td class=\"mono\">{0}</td>", Encode(dc.IPAddress));
                sb.AppendFormat("<td>{0}</td>", Encode(dc.OSVersion));
                sb.AppendFormat("<td>{0}</td>", dc.IsGlobalCatalog ? "✓" : "");
                sb.AppendFormat("<td>{0}</td>", dc.Roles == null ? "" : Encode(string.Join(", ", dc.Roles)));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static void AppendFooter(StringBuilder sb)
        {
            sb.AppendLine("<footer>Généré par ADReplic.</footer>");
        }

        private static void AppendDiagnosticsSection(StringBuilder sb, IReadOnlyList<DetectedIssue> issues)
        {
            sb.AppendLine("<section><h2>Diagnostics</h2>");

            if (issues == null || issues.Count == 0)
            {
                sb.AppendLine("<div class=\"banner ok\">✓ Aucune anomalie de configuration détectée.</div>");
                sb.AppendLine("</section>");
                return;
            }

            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th></th><th>Sévérité</th><th>Code</th><th>Anomalie</th><th>Éléments concernés</th><th>Recommandation</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var issue in issues)
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td><span class=\"dot {0}\"></span></td>", IssueSeverityDotClass(issue.Severity));
                sb.AppendFormat("<td>{0}</td>", Encode(issue.Severity.ToString()));
                sb.AppendFormat("<td class=\"mono\">{0}</td>", Encode(issue.Code));
                sb.AppendFormat("<td>{0}</td>", Encode(issue.Title));
                sb.AppendFormat("<td>{0}</td>", FormatList(issue.AffectedItems));
                sb.AppendFormat("<td>{0}</td>", Encode(issue.Description));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static string IssueSeverityDotClass(IssueSeverity severity)
        {
            switch (severity)
            {
                case IssueSeverity.Critical: return "fail";
                case IssueSeverity.Warning:  return "warn";
                case IssueSeverity.Info:     return "neutral";
                default:                     return "neutral";
            }
        }

        private static void AppendDnsHealthSection(StringBuilder sb, DnsHealthResult dns)
        {
            // Sonde non exécutée : on omet entièrement la section (évite une bandeau vide)
            // dans la GUI/PowerShell tant que la sonde n'est pas branchée (Phase D / E).
            if (dns == null) return;

            sb.AppendLine("<section><h2>Santé DNS</h2>");

            if (dns.Checks == null || dns.Checks.Count == 0)
            {
                sb.AppendLine("<p class=\"empty\">Aucune vérification DNS effectuée.</p></section>");
                return;
            }

            var anomalies = dns.Checks.Count(c => c.Status != DnsCheckStatus.Ok);
            if (anomalies == 0)
            {
                sb.AppendLine("<div class=\"banner ok\">✓ Tous les enregistrements DNS testés sont résolvables.</div>");
            }

            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th></th><th>Enregistrement</th><th>Type</th><th>Statut</th><th>Cible</th><th>Port</th><th>IP</th><th>Détail</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var c in dns.Checks
                .OrderBy(x => x.Status == DnsCheckStatus.Ok ? 1 : 0) // anomalies en tête
                .ThenBy(x => (int)x.Type)
                .ThenBy(x => x.RecordName, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td><span class=\"dot {0}\"></span></td>", DnsStatusDotClass(c.Status));
                sb.AppendFormat("<td class=\"mono\">{0}</td>", Encode(c.RecordName));
                sb.AppendFormat("<td>{0}</td>", DnsTypeLabel(c.Type));
                sb.AppendFormat("<td>{0}</td>", Encode(c.Status.ToString()));
                sb.AppendFormat("<td>{0}</td>", Encode(c.Target));
                sb.AppendFormat("<td class=\"num\">{0}</td>", c.Port.HasValue ? c.Port.Value.ToString(CultureInfo.InvariantCulture) : "—");
                sb.AppendFormat("<td class=\"mono\">{0}</td>", Encode(c.IpAddress));
                sb.AppendFormat("<td>{0}</td>", Encode(c.ErrorMessage));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static string DnsStatusDotClass(DnsCheckStatus s)
        {
            switch (s)
            {
                case DnsCheckStatus.Ok:      return "ok";
                case DnsCheckStatus.Missing: return "warn";
                case DnsCheckStatus.Error:   return "fail";
                default:                     return "neutral";
            }
        }

        private static string DnsTypeLabel(DnsCheckedRecordType t)
        {
            switch (t)
            {
                case DnsCheckedRecordType.SrvLdap:     return "SRV _ldap";
                case DnsCheckedRecordType.SrvKerberos: return "SRV _kerberos";
                case DnsCheckedRecordType.SrvGc:       return "SRV _gc";
                case DnsCheckedRecordType.SrvKpasswd:  return "SRV _kpasswd";
                case DnsCheckedRecordType.ARecord:     return "A";
                default:                               return t.ToString();
            }
        }

        private static void AppendPortHealthSection(StringBuilder sb, PortHealthResult ports)
        {
            if (ports == null) return;

            sb.AppendLine("<section><h2>Santé réseau (tests de ports)</h2>");

            if (ports.Checks == null || ports.Checks.Count == 0)
            {
                sb.AppendLine("<p class=\"empty\">Aucun test de port effectué.</p></section>");
                return;
            }

            var anomalies = ports.Checks.Count(c => c.Status != PortCheckStatus.Open);
            if (anomalies == 0)
            {
                sb.AppendLine("<div class=\"banner ok\">✓ Tous les ports testés sont ouverts sur l'ensemble des contrôleurs.</div>");
            }

            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th></th><th>Contrôleur</th><th>Port</th><th>Service</th><th>Statut</th><th>Temps de réponse</th><th>Détail</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var c in ports.Checks
                .OrderBy(x => x.Status == PortCheckStatus.Open ? 1 : 0) // anomalies en tête
                .ThenBy(x => x.HostName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Port))
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td><span class=\"dot {0}\"></span></td>", PortStatusDotClass(c.Status));
                sb.AppendFormat("<td>{0}</td>", Encode(c.HostName));
                sb.AppendFormat("<td class=\"num\">{0}</td>", c.Port);
                sb.AppendFormat("<td>{0}</td>", Encode(c.ServiceLabel));
                sb.AppendFormat("<td>{0}</td>", Encode(c.Status.ToString()));
                sb.AppendFormat("<td class=\"num\">{0} ms</td>", (int)c.ResponseTime.TotalMilliseconds);
                sb.AppendFormat("<td>{0}</td>", Encode(c.ErrorMessage));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static string PortStatusDotClass(PortCheckStatus s)
        {
            switch (s)
            {
                case PortCheckStatus.Open:    return "ok";
                case PortCheckStatus.Closed:  return "fail";
                case PortCheckStatus.Timeout: return "warn";
                case PortCheckStatus.Error:   return "warn";
                default:                      return "neutral";
            }
        }

        private static void AppendFailuresSection(StringBuilder sb, IReadOnlyList<ReplicationFailureInfo> failures)
        {
            sb.AppendLine("<section><h2>Échecs de réplication en cours</h2>");

            if (failures == null || failures.Count == 0)
            {
                sb.AppendLine("<div class=\"banner ok\">✓ Aucun échec de réplication en cours sur l'ensemble des contrôleurs.</div>");
                sb.AppendLine("</section>");
                return;
            }

            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th></th><th>Destination</th><th>Source</th><th>Premier échec</th><th>Durée</th><th>Tentatives</th><th>Erreur</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var f in failures)
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td><span class=\"dot {0}\"></span></td>", SeverityDotClass(f.Severity));
                sb.AppendFormat("<td>{0}</td>", Encode(f.DestinationDc));
                sb.AppendFormat("<td>{0}</td>", Encode(f.SourceDc));
                sb.AppendFormat("<td>{0}</td>", FormatDate(f.FirstFailureTime));
                sb.AppendFormat("<td>{0}</td>", FormatLatency(f.FailureDuration));
                sb.AppendFormat("<td class=\"num\">{0}</td>", f.ConsecutiveFailureCount);
                sb.AppendFormat("<td>{0}</td>", Encode(f.LastErrorMessage));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static string SeverityDotClass(ReplicationFailureSeverity s)
        {
            switch (s)
            {
                case ReplicationFailureSeverity.Recent: return "warn";
                case ReplicationFailureSeverity.Sustained: return "fail";
                case ReplicationFailureSeverity.Critical: return "fail";
                default: return "neutral";
            }
        }

        private static void AppendSitesSection(StringBuilder sb, IReadOnlyList<SiteInfo> sites)
        {
            if (sites == null || sites.Count == 0) return;
            sb.AppendLine("<section><h2>Sites Active Directory</h2>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>Site</th><th>Localisation</th><th>Contrôleurs</th><th>Sous-réseaux</th><th>Têtes de pont</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var site in sites)
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td>{0}</td>", Encode(site.Name));
                sb.AppendFormat("<td>{0}</td>", Encode(site.Location));
                sb.AppendFormat("<td>{0}</td>", FormatList(site.DomainControllers));
                sb.AppendFormat("<td class=\"mono\">{0}</td>", FormatList(site.Subnets));
                sb.AppendFormat("<td>{0}</td>", FormatList(site.BridgeheadServers));
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static void AppendSiteLinksTable(StringBuilder sb, IReadOnlyList<SiteLinkInfo> links)
        {
            if (links == null || links.Count == 0) return;
            sb.AppendLine("<section><h2>Liens inter-sites</h2>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>Nom</th><th>Sites reliés</th><th>Coût</th><th>Intervalle</th><th>Transport</th><th>Notifications</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var link in links)
            {
                sb.Append("<tr>");
                sb.AppendFormat("<td>{0}</td>", Encode(link.Name));
                sb.AppendFormat("<td>{0}</td>", FormatList(link.Sites));
                sb.AppendFormat("<td class=\"num\">{0}</td>", link.Cost);
                sb.AppendFormat("<td>{0}</td>", FormatInterval(link.ReplicationInterval));
                sb.AppendFormat("<td>{0}</td>", Encode(link.TransportType));
                sb.AppendFormat("<td>{0}</td>", link.ChangeNotificationEnabled ? "✓" : "");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        private static string FormatList(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return "—";
            return Encode(string.Join(", ", values));
        }

        private static string FormatInterval(TimeSpan t)
        {
            if (t.TotalMinutes < 60) return ((int)t.TotalMinutes) + "min";
            if (t.TotalHours < 24) return ((int)t.TotalHours) + "h";
            return ((int)t.TotalDays) + "j";
        }

        private static int StatusWeight(ReplicationLinkStatus s)
        {
            switch (s)
            {
                case ReplicationLinkStatus.Failing:
                case ReplicationLinkStatus.Unreachable: return 0;
                case ReplicationLinkStatus.Warning: return 1;
                case ReplicationLinkStatus.Healthy: return 2;
                default: return 3;
            }
        }

        private static string StatusDotClass(ReplicationLinkStatus s)
        {
            switch (s)
            {
                case ReplicationLinkStatus.Healthy: return "ok";
                case ReplicationLinkStatus.Warning: return "warn";
                case ReplicationLinkStatus.Failing:
                case ReplicationLinkStatus.Unreachable: return "fail";
                default: return "neutral";
            }
        }

        private static string FormatDate(DateTime? d)
        {
            return d.HasValue
                ? d.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"))
                : "—";
        }

        private static string FormatLatency(TimeSpan? t)
        {
            if (!t.HasValue) return "—";
            var v = t.Value;
            if (v.TotalMinutes < 1) return ((int)v.TotalSeconds) + "s";
            if (v.TotalHours < 1) return ((int)v.TotalMinutes) + "min";
            if (v.TotalDays < 1) return ((int)v.TotalHours) + "h" + v.Minutes.ToString("D2");
            return ((int)v.TotalDays) + "j" + v.Hours.ToString("D2") + "h";
        }

        private static string Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        private static string InlineStyles() =>
@"<style>
:root { color-scheme: light; }
* { box-sizing: border-box; }
body { font-family: -apple-system, 'Segoe UI', Roboto, sans-serif; font-size: 13px;
       color: #1b1f23; background: #f5f6f8; margin: 0; padding: 0; }
.hdr { background: #ffffff; padding: 24px 32px; border-bottom: 1px solid #d0d7de; }
.hdr h1 { margin: 0 0 6px; font-size: 22px; font-weight: 600; }
.hdr .meta { margin: 0; color: #586069; font-size: 13px; }
.cards { display: flex; flex-wrap: wrap; gap: 12px; padding: 16px 32px; }
.card { background: #fff; border: 1px solid #d0d7de; border-left: 4px solid #6e7781;
        padding: 12px 18px; min-width: 140px; border-radius: 4px; }
.card .v { font-size: 22px; font-weight: 600; }
.card .l { font-size: 12px; color: #586069; margin-top: 2px; }
.card.ok      { border-left-color: #1a7f37; }
.card.warn    { border-left-color: #9a6700; }
.card.fail    { border-left-color: #d1242f; }
.card.neutral { border-left-color: #6e7781; }
section { padding: 8px 32px 24px; }
section h2 { font-size: 15px; font-weight: 600; margin: 18px 0 10px; }
table { width: 100%; border-collapse: collapse; background: #fff;
        border: 1px solid #d0d7de; border-radius: 4px; overflow: hidden; }
th, td { padding: 8px 10px; text-align: left; border-bottom: 1px solid #eaeef2;
         vertical-align: top; }
th { background: #f6f8fa; font-weight: 600; font-size: 12px; color: #586069; }
tr:last-child td { border-bottom: none; }
td.mono { font-family: Consolas, 'SF Mono', monospace; font-size: 12px; color: #424a53; }
td.num { text-align: right; font-variant-numeric: tabular-nums; }
.dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; background: #6e7781; }
.dot.ok    { background: #1a7f37; }
.dot.warn  { background: #9a6700; }
.dot.fail  { background: #d1242f; }
.empty { color: #6e7781; font-style: italic; }
.banner { padding: 14px 18px; border-radius: 4px; border: 1px solid; font-size: 13px; }
.banner.ok   { background: #ddf4e4; border-color: #2da44e; color: #1a7f37; }
.banner.warn { background: #fff4d6; border-color: #d4a72c; color: #7d4e00; }
.banner.fail { background: #ffebe9; border-color: #d1242f; color: #82071e; }
.health-badge { display: inline-flex; align-items: center; gap: 18px;
                margin-top: 14px; padding: 12px 18px; border-radius: 6px;
                color: #fff; }
.health-badge.ok   { background: #1a7f37; }
.health-badge.warn { background: #9a6700; }
.health-badge.fail { background: #d1242f; }
.health-badge .hb-score { font-size: 24px; font-weight: 700; line-height: 1; }
.health-badge .hb-score span { font-size: 14px; font-weight: 500; opacity: 0.85; }
.health-badge .hb-label { font-size: 13px; font-weight: 600; }
.health-badge .hb-summary { font-size: 12px; opacity: 0.92; margin-top: 2px; }
footer { padding: 18px 32px; color: #6e7781; font-size: 12px; border-top: 1px solid #d0d7de; }
</style>";
    }
}
