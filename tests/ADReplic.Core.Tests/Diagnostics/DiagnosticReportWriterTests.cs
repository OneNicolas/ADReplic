using System;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// La fiche diagnostic copiée dans le presse-papiers doit être autonome :
    /// quelqu'un qui la reçoit par mail ou dans un ticket doit comprendre le
    /// problème et savoir quoi faire sans ouvrir ADReplic.
    /// </summary>
    public class DiagnosticReportWriterTests
    {
        private static ReplicationFailureInfo SampleFailure() => new ReplicationFailureInfo
        {
            DestinationDc = "srv-dc-02.exemple.local",
            SourceDc = "srv-dc-01.exemple.local",
            FirstFailureTime = DateTime.Now.AddDays(-3),
            ConsecutiveFailureCount = 42,
            LastErrorCode = 1722,
            LastErrorMessage = "Le serveur RPC n'est pas disponible.",
            Severity = ReplicationFailureSeverity.Critical
        };

        private static DetectedIssue SampleIssue()
        {
            var issue = new DetectedIssue
            {
                Code = "SITE_NO_SUBNET",
                Severity = IssueSeverity.Warning,
                Title = "Site sans subnet associé : Lyon",
                Description = "Ce site contient des DC mais aucun subnet IP ne lui est rattaché.",
                AffectedItems = new[] { "Lyon" }
            };
            IssueKnowledgeBase.Enrich(issue);
            return issue;
        }

        [Fact]
        public void Failure_plain_text_contains_all_sections()
        {
            var text = DiagnosticReportWriter.ToPlainText(SampleFailure());

            Assert.Contains("Serveur RPC injoignable", text);
            Assert.Contains("srv-dc-02.exemple.local", text);
            Assert.Contains("srv-dc-01.exemple.local", text);
            Assert.Contains("1722", text);
            Assert.Contains("CE QUE CELA SIGNIFIE", text);
            Assert.Contains("CAUSES PROBABLES", text);
            Assert.Contains("QUE VERIFIER", text);
            Assert.Contains("COMMANDES", text);
            Assert.Contains("nslookup", text);
            Assert.Contains("learn.microsoft.com", text);
            Assert.Contains("ADReplic", text);
        }

        [Fact]
        public void Failure_html_is_well_formed_and_escaped()
        {
            var failure = SampleFailure();
            failure.LastErrorMessage = "Message <avec> & caractères \"spéciaux\"";

            var html = DiagnosticReportWriter.ToHtml(failure);

            Assert.StartsWith("<", html.TrimStart());
            Assert.Contains("&lt;avec&gt;", html);
            Assert.Contains("&amp;", html);
            Assert.DoesNotContain("<avec>", html);
            Assert.Contains("<h2", html);
            Assert.Contains("<ol", html);
            Assert.Contains("href=\"https://learn.microsoft.com", html);
        }

        [Fact]
        public void Issue_plain_text_contains_impact_and_recommendation()
        {
            var text = DiagnosticReportWriter.ToPlainText(SampleIssue());

            Assert.Contains("Site sans subnet", text);
            Assert.Contains("SITE_NO_SUBNET", text);
            Assert.Contains("CONSTAT", text);
            Assert.Contains("POURQUOI C'EST IMPORTANT", text);
            Assert.Contains("QUE FAIRE", text);
            Assert.Contains("Lyon", text);
            Assert.Contains("Sites et services AD", text);
        }

        [Fact]
        public void Issue_html_renders_multiline_recommendation_as_list()
        {
            var html = DiagnosticReportWriter.ToHtml(SampleIssue());

            // La recommandation est stockee "1. ...\n2. ..." : en HTML on veut une liste.
            Assert.Contains("<ol", html);
            Assert.Contains("<li>", html);
        }

        [Fact]
        public void Writers_tolerate_null()
        {
            Assert.Equal("", DiagnosticReportWriter.ToPlainText((ReplicationFailureInfo)null));
            Assert.Equal("", DiagnosticReportWriter.ToHtml((DetectedIssue)null));
        }

        [Fact]
        public void Issue_without_knowledge_still_produces_text()
        {
            var issue = new DetectedIssue { Code = "INCONNU", Severity = IssueSeverity.Info, Title = "Titre", Description = "Desc" };

            var text = DiagnosticReportWriter.ToPlainText(issue);

            Assert.Contains("Titre", text);
            Assert.Contains("Desc", text);
            Assert.DoesNotContain("QUE FAIRE", text);
        }
    }
}
