using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Rédige une "fiche diagnostic" autonome pour un échec de réplication ou
    /// une anomalie, en texte brut (ticket, Notepad) et en HTML (Outlook, Teams).
    /// Les deux variantes portent exactement les mêmes sections, dans le même
    /// ordre, pour que le lecteur retrouve ses repères quel que soit le support.
    /// </summary>
    public static class DiagnosticReportWriter
    {
        private const string Footer = "Fiche générée par ADReplic";

        // ------------------------------------------------------------------
        // Modèle intermédiaire : une fiche = un titre, des métadonnées, des sections.
        // Les deux rendus (texte, HTML) consomment ce modèle : une seule logique métier.
        // ------------------------------------------------------------------

        private sealed class Section
        {
            public string Heading;
            public string Paragraph;          // texte libre, ou null
            public IReadOnlyList<string> Items; // puces, ou null
            public bool Ordered;              // liste numérotée (étapes)
            public bool Monospace;            // commandes
        }

        private sealed class Report
        {
            public string Title;
            public List<KeyValuePair<string, string>> Facts = new List<KeyValuePair<string, string>>();
            public List<Section> Sections = new List<Section>();
            public string DocumentationUrl;
        }

        // ------------------------------------------------------------------
        // API publique
        // ------------------------------------------------------------------

        public static string ToPlainText(ReplicationFailureInfo failure)
            => failure == null ? "" : RenderPlainText(Build(failure));

        public static string ToHtml(ReplicationFailureInfo failure)
            => failure == null ? "" : RenderHtml(Build(failure));

        public static string ToPlainText(DetectedIssue issue)
            => issue == null ? "" : RenderPlainText(Build(issue));

        public static string ToHtml(DetectedIssue issue)
            => issue == null ? "" : RenderHtml(Build(issue));

        // ------------------------------------------------------------------
        // Construction du modèle
        // ------------------------------------------------------------------

        private static Report Build(ReplicationFailureInfo f)
        {
            var x = f.Explanation;
            var report = new Report
            {
                Title = "Échec de réplication : " + x.Title,
                DocumentationUrl = x.DocumentationUrl
            };

            report.Facts.Add(Fact("DC de destination", f.DestinationDc));
            report.Facts.Add(Fact("DC source", f.SourceDc));
            report.Facts.Add(Fact("Premier échec", f.FirstFailureTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                                                   + " (depuis " + DurationFormatter.ToHumanText(f.FailureDuration) + ")"));
            report.Facts.Add(Fact("Tentatives consécutives", f.ConsecutiveFailureCount.ToString(CultureInfo.InvariantCulture)));
            report.Facts.Add(Fact("Code d'erreur", f.LastErrorCode.ToString(CultureInfo.InvariantCulture)
                                                   + (string.IsNullOrWhiteSpace(f.LastErrorMessage) ? "" : " - " + f.LastErrorMessage)));
            report.Facts.Add(Fact("Gravité", SeverityLabel(f.Severity)));

            report.Sections.Add(new Section { Heading = "Ce que cela signifie", Paragraph = x.Explanation });
            if (x.ProbableCauses.Count > 0) report.Sections.Add(new Section { Heading = "Causes probables", Items = x.ProbableCauses });
            if (x.Checks.Count > 0)         report.Sections.Add(new Section { Heading = "Que vérifier", Items = x.Checks, Ordered = true });
            if (x.Commands.Count > 0)       report.Sections.Add(new Section { Heading = "Commandes", Items = x.Commands, Monospace = true });
            return report;
        }

        private static Report Build(DetectedIssue i)
        {
            var report = new Report
            {
                Title = "Anomalie : " + i.Title,
                DocumentationUrl = i.DocumentationUrl
            };

            report.Facts.Add(Fact("Code", i.Code));
            report.Facts.Add(Fact("Sévérité", SeverityLabel(i.Severity)));
            if (i.AffectedItems != null && i.AffectedItems.Count > 0)
                report.Facts.Add(Fact("Éléments concernés", string.Join(", ", i.AffectedItems)));

            if (!string.IsNullOrWhiteSpace(i.Description))    report.Sections.Add(new Section { Heading = "Constat", Paragraph = i.Description });
            if (!string.IsNullOrWhiteSpace(i.Impact))         report.Sections.Add(new Section { Heading = "Pourquoi c'est important", Paragraph = i.Impact });
            if (!string.IsNullOrWhiteSpace(i.Recommendation)) report.Sections.Add(new Section { Heading = "Que faire", Items = SplitSteps(i.Recommendation), Ordered = true });
            return report;
        }

        private static KeyValuePair<string, string> Fact(string label, string value)
            => new KeyValuePair<string, string>(label, value ?? "");

        /// <summary>
        /// Les recommandations sont stockées "1. Étape\n2. Étape" : on en fait une
        /// liste d'étapes, numéro retiré (le rendu numérote lui-même).
        /// </summary>
        private static IReadOnlyList<string> SplitSteps(string text)
        {
            return text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.Trim())
                       .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\d+\.\s*", ""))
                       .Where(l => l.Length > 0)
                       .ToList();
        }

        private static string SeverityLabel(ReplicationFailureSeverity s)
        {
            switch (s)
            {
                // Pas de durée dans l'étiquette : un DC injoignable est Critique d'emblée,
                // quelle que soit l'ancienneté de l'échec.
                case ReplicationFailureSeverity.Recent:    return "Récent";
                case ReplicationFailureSeverity.Sustained: return "Persistant";
                case ReplicationFailureSeverity.Critical:  return "Critique";
                default:                                   return "Inconnue";
            }
        }

        private static string SeverityLabel(IssueSeverity s)
        {
            switch (s)
            {
                case IssueSeverity.Critical: return "Critique";
                case IssueSeverity.Warning:  return "Avertissement";
                default:                     return "Information";
            }
        }

        // ------------------------------------------------------------------
        // Rendu texte brut
        // ------------------------------------------------------------------

        private static string RenderPlainText(Report r)
        {
            var sb = new StringBuilder();
            sb.AppendLine(r.Title);
            sb.AppendLine(new string('=', Math.Min(r.Title.Length, 72)));
            sb.AppendLine();

            foreach (var fact in r.Facts)
                sb.AppendLine(fact.Key + " : " + fact.Value);
            sb.AppendLine();

            foreach (var s in r.Sections)
            {
                sb.AppendLine(RemoveDiacritics(s.Heading).ToUpperInvariant());
                sb.AppendLine(new string('-', Math.Min(s.Heading.Length, 72)));
                if (s.Paragraph != null) sb.AppendLine(s.Paragraph);
                if (s.Items != null)
                {
                    for (int i = 0; i < s.Items.Count; i++)
                        sb.AppendLine((s.Ordered ? (i + 1) + ". " : "- ") + s.Items[i]);
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(r.DocumentationUrl))
            {
                sb.AppendLine("Documentation : " + r.DocumentationUrl);
                sb.AppendLine();
            }

            sb.Append(Footer + " le " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Les titres de section en majuscules sont dépouillés de leurs accents :
        /// "QUE VÉRIFIER" surprend moins en "QUE VERIFIER" dans un ticket ASCII.
        /// </summary>
        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ------------------------------------------------------------------
        // Rendu HTML (fragment, sans <html>/<body> : le presse-papiers et les
        // clients mail préfèrent un fragment ; styles inline car Outlook ignore <style>).
        // ------------------------------------------------------------------

        private static string RenderHtml(Report r)
        {
            const string font = "font-family:Segoe UI,Arial,sans-serif;font-size:10.5pt;color:#222;";
            var sb = new StringBuilder();
            sb.Append("<div style=\"").Append(font).Append("\">");
            sb.Append("<h2 style=\"font-size:14pt;margin:0 0 8px 0;color:#1a3b6e;\">").Append(E(r.Title)).Append("</h2>");

            sb.Append("<table style=\"border-collapse:collapse;margin:0 0 12px 0;\">");
            foreach (var fact in r.Facts)
            {
                sb.Append("<tr><td style=\"padding:2px 12px 2px 0;color:#666;white-space:nowrap;\">").Append(E(fact.Key))
                  .Append("</td><td style=\"padding:2px 0;\">").Append(E(fact.Value)).Append("</td></tr>");
            }
            sb.Append("</table>");

            foreach (var s in r.Sections)
            {
                sb.Append("<h3 style=\"font-size:11pt;margin:12px 0 4px 0;color:#1a3b6e;\">").Append(E(s.Heading)).Append("</h3>");
                if (s.Paragraph != null) sb.Append("<p style=\"margin:0 0 6px 0;\">").Append(E(s.Paragraph)).Append("</p>");
                if (s.Items != null)
                {
                    sb.Append(s.Ordered ? "<ol style=\"margin:0 0 6px 20px;padding:0;\">" : "<ul style=\"margin:0 0 6px 20px;padding:0;\">");
                    foreach (var item in s.Items)
                    {
                        sb.Append("<li>");
                        if (s.Monospace) sb.Append("<code style=\"font-family:Consolas,monospace;background:#f3f3f3;padding:1px 4px;\">").Append(E(item)).Append("</code>");
                        else             sb.Append(E(item));
                        sb.Append("</li>");
                    }
                    sb.Append(s.Ordered ? "</ol>" : "</ul>");
                }
            }

            if (!string.IsNullOrEmpty(r.DocumentationUrl))
            {
                sb.Append("<p style=\"margin:12px 0 0 0;\">Documentation : <a href=\"").Append(E(r.DocumentationUrl)).Append("\">")
                  .Append(E(r.DocumentationUrl)).Append("</a></p>");
            }

            sb.Append("<p style=\"margin:12px 0 0 0;color:#888;font-size:9pt;\">").Append(E(Footer))
              .Append(" le ").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)).Append("</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private static string E(string s) => WebUtility.HtmlEncode(s ?? "");
    }
}
