using ADReplic.Core.Diagnostics.Issues;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// La base de connaissance doit couvrir chaque code émis par les détecteurs
    /// livrés, et n'enrichir que les champs laissés vides par le détecteur.
    /// </summary>
    public class IssueKnowledgeBaseTests
    {
        // Liste exhaustive des codes émis par IssueAggregator.BuildDefaultDetectors().
        // A completer a chaque nouveau detecteur : le test echoue sinon, volontairement.
        public static readonly string[] AllDetectorCodes =
        {
            "DC_UNREACHABLE", "DC_ISOLATED",
            "OS_UNSUPPORTED", "OS_OUT_OF_EXTENDED_SUPPORT", "OS_UNKNOWN",
            "DOMAIN_SINGLE_DC",
            "SITE_NO_BRIDGEHEAD", "SITE_NO_SUBNET", "SITE_NO_DC", "SITE_LINK_COST_ABERRANT",
            "DNS_SRV_CRITICAL_MISSING", "DNS_SRV_OPTIONAL_MISSING",
            "DNS_RESOLUTION_ERROR", "DNS_DC_A_RECORD_MISSING",
            "PORT_CRITICAL_CLOSED", "PORT_CLOSED_OR_FILTERED",
            "DNS_SRV_POINTS_TO_DEAD_DC",
            "DC_CLOCK_SKEW",
            "REPLICATION_LINK_FAILING"
        };

        [Fact]
        public void Every_detector_code_has_an_entry()
        {
            foreach (var code in AllDetectorCodes)
            {
                var entry = IssueKnowledgeBase.Lookup(code);

                Assert.NotNull(entry);
                Assert.False(string.IsNullOrWhiteSpace(entry.Impact), $"{code} : impact vide");
                Assert.False(string.IsNullOrWhiteSpace(entry.Recommendation), $"{code} : recommandation vide");
            }
        }

        [Fact]
        public void Lookup_returns_null_for_unknown_code()
        {
            Assert.Null(IssueKnowledgeBase.Lookup("CODE_QUI_N_EXISTE_PAS"));
            Assert.Null(IssueKnowledgeBase.Lookup(null));
        }

        [Fact]
        public void Enrich_fills_empty_fields_only()
        {
            var issue = new DetectedIssue
            {
                Code = "DC_ISOLATED",
                Impact = "Impact fourni par le detecteur"
            };

            IssueKnowledgeBase.Enrich(issue);

            Assert.Equal("Impact fourni par le detecteur", issue.Impact);
            Assert.False(string.IsNullOrWhiteSpace(issue.Recommendation));
        }

        [Fact]
        public void Enrich_tolerates_null_and_unknown_issue()
        {
            IssueKnowledgeBase.Enrich(null);

            var unknown = new DetectedIssue { Code = "INCONNU" };
            IssueKnowledgeBase.Enrich(unknown);

            Assert.Null(unknown.Impact);
            Assert.Null(unknown.Recommendation);
        }
    }
}
