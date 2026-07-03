using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Comparison;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Comparison
{
    /// <summary>
    /// Vérifie le comportement de l'algorithme de diff sur chaque catégorie de modèle :
    /// Added / Removed / Changed et calcul du HealthScoreDelta.
    /// </summary>
    public class AuditDifferTests
    {
        // === Helpers de construction ===========================================

        private static AuditSnapshot Empty(string forest = "exemple.local")
        {
            return new AuditSnapshot
            {
                ForestName = forest,
                GeneratedAt = new DateTime(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc),
                DomainControllers = new List<DomainControllerInfo>(),
                ReplicationLinks = new List<ReplicationLink>(),
                ReplicationFailures = new List<ReplicationFailureInfo>(),
                Sites = new List<SiteInfo>(),
                SiteLinks = new List<SiteLinkInfo>(),
                Issues = new List<DetectedIssue>()
            };
        }

        private static DomainControllerInfo Dc(
            string host, string os = "Windows Server 2022", string site = "Paris", string ip = "10.0.0.1") =>
            new DomainControllerInfo
            {
                HostName = host,
                OSVersion = os,
                SiteName = site,
                IPAddress = ip,
                Domain = "exemple.local",
                Forest = "exemple.local"
            };

        private static ReplicationLink Link(
            string dest, string src, string nc = "DC=exemple,DC=local",
            ReplicationLinkStatus status = ReplicationLinkStatus.Healthy) =>
            new ReplicationLink
            {
                DestinationDc = dest, SourceDc = src, NamingContext = nc, Status = status
            };

        private static ReplicationFailureInfo Failure(
            string dest, string src, int code = 8524, ReplicationFailureSeverity sev = ReplicationFailureSeverity.Recent,
            int consecutive = 3) =>
            new ReplicationFailureInfo
            {
                DestinationDc = dest, SourceDc = src,
                LastErrorCode = code, LastErrorMessage = "erreur de test",
                Severity = sev, ConsecutiveFailureCount = consecutive,
                FirstFailureTime = DateTime.UtcNow.AddHours(-1)
            };

        private static SiteInfo Site(string name, string[] dcs = null, string[] subnets = null, string[] bridgeheads = null) =>
            new SiteInfo
            {
                Name = name,
                DomainControllers = dcs ?? Array.Empty<string>(),
                Subnets = subnets ?? Array.Empty<string>(),
                BridgeheadServers = bridgeheads ?? Array.Empty<string>()
            };

        private static SiteLinkInfo SLink(string name, int cost = 100, string[] sites = null) =>
            new SiteLinkInfo
            {
                Name = name, Cost = cost,
                Sites = sites ?? Array.Empty<string>(),
                TransportType = "IP",
                ReplicationInterval = TimeSpan.FromMinutes(180)
            };

        private static DetectedIssue Issue(string code, IssueSeverity sev = IssueSeverity.Warning, params string[] items) =>
            new DetectedIssue
            {
                Code = code, Severity = sev, Title = code + " title",
                AffectedItems = items ?? Array.Empty<string>()
            };

        private static DnsCheckResult Dns(string name, DnsCheckedRecordType type,
            DnsCheckStatus status = DnsCheckStatus.Ok, string target = null) =>
            new DnsCheckResult
            {
                RecordName = name, Type = type, Status = status, Target = target
            };

        private static PortCheckResult Port(string host, int port,
            PortCheckStatus status = PortCheckStatus.Open, string label = null) =>
            new PortCheckResult
            {
                HostName = host, Port = port, Status = status, ServiceLabel = label ?? "svc"
            };

        private static HealthScore Score(int value, HealthLevel level, int rep = 100, int? dns = null, int? port = null) =>
            new HealthScore
            {
                Value = value, Level = level,
                ReplicationScore = rep, DnsScore = dns, PortScore = port
            };

        // === Validation des arguments =========================================

        [Fact]
        public void Throws_when_reference_is_null()
        {
            Assert.Throws<ArgumentNullException>(() => AuditDiffer.Compute(null, Empty()));
        }

        [Fact]
        public void Throws_when_current_is_null()
        {
            Assert.Throws<ArgumentNullException>(() => AuditDiffer.Compute(Empty(), null));
        }

        // === Diff vide ========================================================

        [Fact]
        public void Empty_snapshots_produce_empty_diff()
        {
            var diff = AuditDiffer.Compute(Empty(), Empty());

            Assert.True(diff.IsEmpty);
            Assert.NotNull(diff.DomainControllers);
            Assert.True(diff.DomainControllers.IsEmpty);
            Assert.True(diff.Issues.IsEmpty);
        }

        [Fact]
        public void Null_inner_collections_are_treated_as_empty()
        {
            // Snapshots minimaux où les listes ne sont pas initialisées.
            var a = new AuditSnapshot { ForestName = "exemple.local" };
            var b = new AuditSnapshot { ForestName = "exemple.local" };

            var diff = AuditDiffer.Compute(a, b);

            Assert.True(diff.IsEmpty);
        }

        // === Métadonnées et forêt =============================================

        [Fact]
        public void Carries_forest_names_and_generation_dates()
        {
            var a = Empty("foret-a.local");
            a.GeneratedAt = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            var b = Empty("foret-b.local");
            b.GeneratedAt = new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc);

            var diff = AuditDiffer.Compute(a, b);

            Assert.Equal("foret-a.local", diff.ReferenceForestName);
            Assert.Equal("foret-b.local", diff.CurrentForestName);
            Assert.Equal(a.GeneratedAt, diff.ReferenceGeneratedAt);
            Assert.Equal(b.GeneratedAt, diff.CurrentGeneratedAt);
        }

        [Fact]
        public void DifferentForests_is_true_when_forest_names_differ()
        {
            var diff = AuditDiffer.Compute(Empty("foret-a.local"), Empty("foret-b.local"));
            Assert.True(diff.DifferentForests);
        }

        [Fact]
        public void DifferentForests_is_false_for_identical_forest_names_case_insensitive()
        {
            var diff = AuditDiffer.Compute(Empty("Exemple.Local"), Empty("exemple.local"));
            Assert.False(diff.DifferentForests);
        }

        // === Domain Controllers ==============================================

        [Fact]
        public void Detects_added_dc()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC01"), Dc("DC02") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DomainControllers.Added);
            Assert.Equal("DC02", diff.DomainControllers.Added[0].HostName);
            Assert.Empty(diff.DomainControllers.Removed);
            Assert.Empty(diff.DomainControllers.Changed);
        }

        [Fact]
        public void Detects_removed_dc()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01"), Dc("DC02") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC01") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DomainControllers.Removed);
            Assert.Equal("DC02", diff.DomainControllers.Removed[0].HostName);
        }

        [Fact]
        public void Detects_changed_dc_os_version()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01", os: "Windows Server 2019") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC01", os: "Windows Server 2022") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DomainControllers.Changed);
            Assert.Equal("Windows Server 2019", diff.DomainControllers.Changed[0].Before.OSVersion);
            Assert.Equal("Windows Server 2022", diff.DomainControllers.Changed[0].After.OSVersion);
        }

        [Fact]
        public void Detects_changed_dc_site()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01", site: "Paris") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC01", site: "Lyon") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DomainControllers.Changed);
        }

        [Fact]
        public void Detects_changed_dc_reachability()
        {
            // Un DC qui bascule joignable -> injoignable reste présent dans
            // l'inventaire (il n'est ni Added ni Removed) : c'est le flag
            // IsReachable qui doit le faire apparaître dans les Modifs.
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01") };
            var b = Empty();
            var down = Dc("DC01");
            down.IsReachable = false;
            b.DomainControllers = new[] { down };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DomainControllers.Changed);
            Assert.True(diff.DomainControllers.Changed[0].Before.IsReachable);
            Assert.False(diff.DomainControllers.Changed[0].After.IsReachable);
        }

        [Fact]
        public void Does_not_detect_dc_change_when_only_discoveredAt_differs()
        {
            var a = Empty();
            var dc1 = Dc("DC01");
            dc1.DiscoveredAt = new DateTime(2026, 1, 1);
            a.DomainControllers = new[] { dc1 };

            var b = Empty();
            var dc2 = Dc("DC01");
            dc2.DiscoveredAt = new DateTime(2026, 5, 23);
            b.DomainControllers = new[] { dc2 };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Empty(diff.DomainControllers.Changed);
        }

        [Fact]
        public void Dc_matching_is_case_insensitive_on_hostname()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("dc01.exemple.local") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC01.exemple.local") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.True(diff.DomainControllers.IsEmpty);
        }

        // === Replication Links ===============================================

        [Fact]
        public void Detects_changed_link_status_only()
        {
            var a = Empty();
            a.ReplicationLinks = new[]
            {
                Link("DC02", "DC01", status: ReplicationLinkStatus.Healthy)
            };
            var b = Empty();
            b.ReplicationLinks = new[]
            {
                Link("DC02", "DC01", status: ReplicationLinkStatus.Failing)
            };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.ReplicationLinks.Changed);
            Assert.Equal(ReplicationLinkStatus.Healthy, diff.ReplicationLinks.Changed[0].Before.Status);
            Assert.Equal(ReplicationLinkStatus.Failing, diff.ReplicationLinks.Changed[0].After.Status);
        }

        [Fact]
        public void Link_is_keyed_by_destination_source_and_naming_context()
        {
            var a = Empty();
            a.ReplicationLinks = new[]
            {
                Link("DC02", "DC01", nc: "DC=exemple,DC=local"),
                Link("DC02", "DC01", nc: "CN=Schema,CN=Configuration,DC=exemple,DC=local")
            };
            var b = Empty();
            b.ReplicationLinks = new[]
            {
                Link("DC02", "DC01", nc: "DC=exemple,DC=local", status: ReplicationLinkStatus.Failing),
                Link("DC02", "DC01", nc: "CN=Schema,CN=Configuration,DC=exemple,DC=local")
            };

            var diff = AuditDiffer.Compute(a, b);

            // Un seul des deux liens (même source/dest mais NC différent) a changé.
            Assert.Single(diff.ReplicationLinks.Changed);
        }

        // === Replication Failures ============================================

        [Fact]
        public void Detects_new_failure()
        {
            var a = Empty();
            var b = Empty();
            b.ReplicationFailures = new[] { Failure("DC02", "DC01") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.ReplicationFailures.Added);
        }

        [Fact]
        public void Detects_failure_severity_change()
        {
            var a = Empty();
            a.ReplicationFailures = new[] { Failure("DC02", "DC01", sev: ReplicationFailureSeverity.Recent) };
            var b = Empty();
            b.ReplicationFailures = new[] { Failure("DC02", "DC01", sev: ReplicationFailureSeverity.Critical) };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.ReplicationFailures.Changed);
            Assert.Equal(ReplicationFailureSeverity.Critical, diff.ReplicationFailures.Changed[0].After.Severity);
        }

        // === Sites ===========================================================

        [Fact]
        public void Detects_changed_site_bridgeheads()
        {
            var a = Empty();
            a.Sites = new[] { Site("Paris", bridgeheads: new[] { "DC01" }) };
            var b = Empty();
            b.Sites = new[] { Site("Paris", bridgeheads: new[] { "DC01", "DC02" }) };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.Sites.Changed);
        }

        [Fact]
        public void Site_change_is_order_independent_on_dc_list()
        {
            var a = Empty();
            a.Sites = new[] { Site("Paris", dcs: new[] { "DC01", "DC02" }) };
            var b = Empty();
            b.Sites = new[] { Site("Paris", dcs: new[] { "DC02", "DC01" }) };

            var diff = AuditDiffer.Compute(a, b);

            Assert.True(diff.Sites.IsEmpty);
        }

        // === Site Links ======================================================

        [Fact]
        public void Detects_changed_sitelink_cost()
        {
            var a = Empty();
            a.SiteLinks = new[] { SLink("Paris-Lyon", cost: 100) };
            var b = Empty();
            b.SiteLinks = new[] { SLink("Paris-Lyon", cost: 500) };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.SiteLinks.Changed);
            Assert.Equal(100, diff.SiteLinks.Changed[0].Before.Cost);
            Assert.Equal(500, diff.SiteLinks.Changed[0].After.Cost);
        }

        // === Issues ==========================================================

        [Fact]
        public void Detects_added_issue()
        {
            var a = Empty();
            var b = Empty();
            b.Issues = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical, "DC02") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.Issues.Added);
        }

        [Fact]
        public void Same_issue_code_with_different_affected_items_are_distinct()
        {
            // Deux issues même code mais sur des éléments différents = deux entités.
            // Si on passe d'une à l'autre, c'est Added + Removed, pas Changed.
            var a = Empty();
            a.Issues = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical, "DC01") };
            var b = Empty();
            b.Issues = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical, "DC02") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.Issues.Added);
            Assert.Single(diff.Issues.Removed);
            Assert.Empty(diff.Issues.Changed);
        }

        [Fact]
        public void Issue_match_is_independent_of_affected_items_order()
        {
            var a = Empty();
            a.Issues = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical, "DC01", "DC02") };
            var b = Empty();
            b.Issues = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical, "DC02", "DC01") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.True(diff.Issues.IsEmpty);
        }

        // === DNS / Ports =====================================================

        [Fact]
        public void Detects_changed_dns_status()
        {
            var a = Empty();
            a.DnsHealth = new DnsHealthResult
            {
                Checks = new[] { Dns("_ldap._tcp.dc._msdcs.exemple.local", DnsCheckedRecordType.SrvLdap, DnsCheckStatus.Ok, "dc01.exemple.local") }
            };
            var b = Empty();
            b.DnsHealth = new DnsHealthResult
            {
                Checks = new[] { Dns("_ldap._tcp.dc._msdcs.exemple.local", DnsCheckedRecordType.SrvLdap, DnsCheckStatus.Missing, "dc01.exemple.local") }
            };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DnsChecks.Changed);
            Assert.Equal(DnsCheckStatus.Missing, diff.DnsChecks.Changed[0].After.Status);
        }

        [Fact]
        public void Null_dnsHealth_is_treated_as_empty_checks()
        {
            var a = Empty();
            var b = Empty();
            b.DnsHealth = new DnsHealthResult
            {
                Checks = new[] { Dns("_ldap._tcp.dc._msdcs.exemple.local", DnsCheckedRecordType.SrvLdap) }
            };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.DnsChecks.Added);
        }

        [Fact]
        public void Detects_changed_port_status()
        {
            var a = Empty();
            a.PortHealth = new PortHealthResult
            {
                Checks = new[] { Port("dc01", 389, PortCheckStatus.Open, "LDAP") }
            };
            var b = Empty();
            b.PortHealth = new PortHealthResult
            {
                Checks = new[] { Port("dc01", 389, PortCheckStatus.Closed, "LDAP") }
            };

            var diff = AuditDiffer.Compute(a, b);

            Assert.Single(diff.PortChecks.Changed);
        }

        // === Health Score ====================================================

        [Fact]
        public void Computes_score_delta_when_both_scores_present()
        {
            var a = Empty();
            a.HealthScore = Score(95, HealthLevel.Excellent, rep: 95, dns: 100, port: 90);
            var b = Empty();
            b.HealthScore = Score(70, HealthLevel.Warning, rep: 70, dns: 80, port: 60);

            var diff = AuditDiffer.Compute(a, b);

            Assert.NotNull(diff.Score);
            Assert.Equal(95, diff.Score.ValueBefore);
            Assert.Equal(70, diff.Score.ValueAfter);
            Assert.Equal(-25, diff.Score.Delta);
            Assert.True(diff.Score.LevelChanged);
            Assert.True(diff.Score.LevelDegraded);
        }

        [Fact]
        public void Score_delta_is_null_when_either_score_is_missing()
        {
            var a = Empty();
            a.HealthScore = Score(100, HealthLevel.Excellent);
            var b = Empty();
            // b.HealthScore reste null

            var diff = AuditDiffer.Compute(a, b);

            Assert.Null(diff.Score);
        }

        [Fact]
        public void LevelDegraded_is_false_when_level_improves()
        {
            var a = Empty();
            a.HealthScore = Score(50, HealthLevel.Critical);
            var b = Empty();
            b.HealthScore = Score(80, HealthLevel.Warning);

            var diff = AuditDiffer.Compute(a, b);

            Assert.True(diff.Score.LevelChanged);
            Assert.False(diff.Score.LevelDegraded);
        }

        // === Robustesse ======================================================

        [Fact]
        public void Null_items_in_list_are_silently_ignored()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01"), null, Dc("DC02") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC01"), Dc("DC02") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.True(diff.DomainControllers.IsEmpty);
        }

        [Fact]
        public void IsEmpty_combines_all_categories()
        {
            var a = Empty();
            a.DomainControllers = new[] { Dc("DC01") };
            var b = Empty();
            b.DomainControllers = new[] { Dc("DC02") };

            var diff = AuditDiffer.Compute(a, b);

            Assert.False(diff.IsEmpty);
        }
    }
}
