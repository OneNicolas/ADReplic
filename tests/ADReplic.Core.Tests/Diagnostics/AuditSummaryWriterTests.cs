using System;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// La phrase de synthèse doit être lisible par un non-expert : pas de code,
    /// pas de GUID, les faits importants d'abord (DC injoignables, échecs, anomalies).
    /// </summary>
    public class AuditSummaryWriterTests
    {
        private static DomainControllerInfo Dc(string name, bool reachable = true) =>
            new DomainControllerInfo { HostName = name, Domain = "exemple.local", IsReachable = reachable };

        private static ReplicationFailureInfo Failure(string dest, string src, int code, double hoursAgo) =>
            new ReplicationFailureInfo
            {
                DestinationDc = dest, SourceDc = src, LastErrorCode = code,
                FirstFailureTime = DateTime.Now.AddHours(-hoursAgo), ConsecutiveFailureCount = 3
            };

        private static DetectedIssue Issue(string code, IssueSeverity sev) =>
            new DetectedIssue { Code = code, Severity = sev, Title = code };

        [Fact]
        public void Null_snapshot_yields_empty_string()
        {
            Assert.Equal("", AuditSummaryWriter.Write(null));
        }

        [Fact]
        public void Healthy_forest_is_reassuring_and_mentions_dc_count()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02"), Dc("DC03") },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new DetectedIssue[0]
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.StartsWith("Tout fonctionne", text);
            Assert.Contains("3 contrôleurs de domaine", text);
            Assert.Contains("aucun échec de réplication", text);
            Assert.Contains("aucune anomalie", text);
        }

        [Fact]
        public void Single_failure_names_both_dcs_duration_and_clear_title()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02") },
                ReplicationFailures = new[] { Failure("DC02", "DC01", 1722, hoursAgo: 72) },
                Issues = new DetectedIssue[0]
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.Contains("1 échec de réplication", text);
            Assert.Contains("DC01", text);
            Assert.Contains("DC02", text);
            Assert.Contains("3 j", text);
            Assert.Contains("RPC injoignable", text);
            Assert.DoesNotContain("1722", text);
        }

        [Fact]
        public void Several_failures_are_counted_not_listed()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02"), Dc("DC03") },
                ReplicationFailures = new[]
                {
                    Failure("DC02", "DC01", 1722, 2),
                    Failure("DC03", "DC01", 8453, 50),
                    Failure("DC03", "DC02", 1256, 1)
                },
                Issues = new DetectedIssue[0]
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.Contains("3 échecs de réplication", text);
            Assert.Contains("le plus ancien", text);
        }

        [Fact]
        public void Unreachable_dcs_are_mentioned_first()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02", reachable: false), Dc("DC03", reachable: false) },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new[] { Issue("DC_UNREACHABLE", IssueSeverity.Critical) }
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.StartsWith("2 contrôleurs de domaine sur 3 sont injoignables", text);
        }

        [Fact]
        public void Issues_are_counted_with_critical_breakdown()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01") },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new[]
                {
                    Issue("A", IssueSeverity.Critical),
                    Issue("B", IssueSeverity.Warning),
                    Issue("C", IssueSeverity.Info)
                }
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.Contains("3 anomalies", text);
            Assert.Contains("1 critique", text);
        }

        [Fact]
        public void Synthetic_unreachable_failure_is_not_counted_twice()
        {
            // Vecu sur la foret de test : un DC eteint produit un echec synthetique
            // (code -1) ; le bandeau disait "1 DC injoignable ; 1 echec ... (dC injoignable)".
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02", reachable: false) },
                ReplicationFailures = new[] { Failure("DC02", "(injoignable)", ReplicationFailureProbe.UnreachableErrorCode, 0.01) },
                Issues = new[] { Issue("DC_UNREACHABLE", IssueSeverity.Critical) }
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.Contains("1 contrôleur de domaine sur 2 est injoignable", text);
            Assert.DoesNotContain("échec de réplication", text);
            Assert.DoesNotContain("dC", text);
        }

        [Fact]
        public void Consequences_of_an_unreachable_dc_are_not_counted_as_anomalies()
        {
            // Un DC éteint produit DC_UNREACHABLE + ports + SRV orphelins : tout est déjà
            // dit par "1 DC injoignable". Les recompter en "11 anomalies" noie l'essentiel.
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02", reachable: false) },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new[]
                {
                    new DetectedIssue { Code = "DC_UNREACHABLE", Severity = IssueSeverity.Critical, RelatedHost = "DC02" },
                    new DetectedIssue { Code = "PORT_CRITICAL_CLOSED", Severity = IssueSeverity.Critical, RelatedHost = "DC02" },
                    new DetectedIssue { Code = "DNS_SRV_POINTS_TO_DEAD_DC", Severity = IssueSeverity.Warning, RelatedHost = "DC02" }
                }
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.Contains("1 contrôleur de domaine sur 2 est injoignable", text);
            Assert.DoesNotContain("anomalie", text);
        }

        [Fact]
        public void Unrelated_anomalies_are_still_counted_next_to_an_unreachable_dc()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02", reachable: false) },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new[]
                {
                    new DetectedIssue { Code = "DC_UNREACHABLE", Severity = IssueSeverity.Critical, RelatedHost = "DC02" },
                    new DetectedIssue { Code = "PORT_CRITICAL_CLOSED", Severity = IssueSeverity.Critical, RelatedHost = "DC02" },
                    new DetectedIssue { Code = "PORT_CRITICAL_CLOSED", Severity = IssueSeverity.Critical, RelatedHost = "DC01" },
                    new DetectedIssue { Code = "SITE_NO_SUBNET", Severity = IssueSeverity.Warning }
                }
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.Contains("2 anomalies de configuration à traiter dont 1 critique", text);
        }

        private static ReplicationLink Link(string dest, string src, ReplicationLinkStatus status)
            => new ReplicationLink { DestinationDc = dest, SourceDc = src, NamingContext = "DC=exemple,DC=local", Status = status };

        [Fact]
        public void Failing_links_without_active_failures_are_never_reported_as_all_good()
        {
            // Vu sur la forêt de test juste après le redémarrage d'un DC : 4 liens en échec
            // dans la matrice (dernière tentative ratée pendant le reboot), aucun échec
            // actif remonté, aucune anomalie — le score était Critique et la phrase disait
            // « Tout fonctionne ». La phrase doit suivre la matrice.
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02") },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new DetectedIssue[0],
                ReplicationLinks = new[]
                {
                    Link("DC01", "DC02", ReplicationLinkStatus.Failing),
                    Link("DC01", "DC02", ReplicationLinkStatus.Failing),
                    Link("DC02", "DC01", ReplicationLinkStatus.Healthy),
                    Link("DC02", "DC01", ReplicationLinkStatus.Warning)
                }
            };

            var text = AuditSummaryWriter.Write(snap);

            Assert.DoesNotContain("Tout fonctionne", text);
            Assert.Contains("2 liens de réplication sur 4 en échec à la dernière tentative", text);
            Assert.Contains("1 lien en avertissement", text);
        }

        [Fact]
        public void Healthy_links_keep_the_all_good_sentence()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02") },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new DetectedIssue[0],
                ReplicationLinks = new[] { Link("DC01", "DC02", ReplicationLinkStatus.Healthy) }
            };

            Assert.StartsWith("Tout fonctionne", AuditSummaryWriter.Write(snap));
        }

        [Fact]
        public void Not_completed_audit_gets_a_neutral_sentence_that_says_no_data_was_collected()
        {
            // Lot 2 v0.8.0 : après un audit en erreur, les onglets vides ne doivent pas
            // ressembler à une forêt saine. La phrase doit lever l'ambiguïté.
            var text = AuditSummaryWriter.WriteNotCompleted("Identifiants refusés par le DC.");

            Assert.Contains("n'a pas abouti", text);
            Assert.Contains("Identifiants refusés par le DC.", text);
            Assert.Contains("Aucune donnée n'a été collectée", text);
            Assert.DoesNotContain("Tout fonctionne", text);
        }

        [Fact]
        public void Not_completed_audit_after_discovery_speaks_of_partial_data()
        {
            // Vu sur la forêt de test, PDC éteint : la découverte avait listé les 2 DC
            // puis une étape suivante a échoué. « Aucune donnée collectée » était faux.
            var text = AuditSummaryWriter.WriteNotCompleted("Contrôleur de domaine injoignable.", partialData: true);

            Assert.Contains("n'a pas abouti", text);
            Assert.Contains("partielles", text);
            Assert.DoesNotContain("Aucune donnée n'a été collectée", text);
        }

        [Fact]
        public void Not_completed_audit_without_explanation_still_reads_well()
        {
            var text = AuditSummaryWriter.WriteNotCompleted(null);

            Assert.Equal("L'audit n'a pas abouti. Aucune donnée n'a été collectée : les onglets vides ne reflètent pas l'état de la forêt.", text);
        }

        [Fact]
        public void Acronym_titles_keep_their_capitals_mid_sentence()
        {
            var snap = new AuditSnapshot
            {
                DomainControllers = new[] { Dc("DC01"), Dc("DC02") },
                ReplicationFailures = new[] { Failure("DC02", "DC01", 1722, 2) },
                Issues = new DetectedIssue[0]
            };

            var text = AuditSummaryWriter.Write(snap);

            // "Serveur RPC injoignable" -> "serveur RPC injoignable" (initiale abaissee, sigle intact).
            Assert.Contains("(serveur RPC injoignable)", text);
        }

        [Fact]
        public void Single_dc_mode_is_stated()
        {
            var snap = new AuditSnapshot
            {
                IsSingleDcMode = true,
                DomainControllers = new[] { Dc("DC01") },
                ReplicationFailures = new ReplicationFailureInfo[0],
                Issues = new DetectedIssue[0]
            };

            Assert.Contains("DC seul", AuditSummaryWriter.Write(snap));
        }
    }
}
