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
    /// Vérifie les six critères d'alerte appliqués par SignificantChangeDetector
    /// sur un AuditDiff précalculé. Construit les diffs à la main pour isoler
    /// le détecteur de la logique de AuditDiffer.
    /// </summary>
    public class SignificantChangeDetectorTests
    {
        // === Helpers ============================================================

        private static AuditDiff EmptyDiff() => new AuditDiff();

        private static DetectedIssue Issue(string code, IssueSeverity sev) =>
            new DetectedIssue { Code = code, Severity = sev, Title = code };

        private static DomainControllerInfo Dc(string host) =>
            new DomainControllerInfo { HostName = host };

        private static ReplicationFailureInfo Failure(string dest, string src, int code = 8524) =>
            new ReplicationFailureInfo
            {
                DestinationDc = dest, SourceDc = src,
                LastErrorCode = code, LastErrorMessage = "test",
                Severity = ReplicationFailureSeverity.Recent
            };

        private static DnsCheckResult Dns(string name, DnsCheckedRecordType type, DnsCheckStatus status) =>
            new DnsCheckResult { RecordName = name, Type = type, Status = status, Target = "dc01.exemple.local" };

        private static PortCheckResult Port(int port, PortCheckStatus status, string host = "dc01") =>
            new PortCheckResult { HostName = host, Port = port, Status = status, ServiceLabel = "svc" };

        // === Préconditions =====================================================

        [Fact]
        public void Throws_when_diff_is_null()
        {
            Assert.Throws<ArgumentNullException>(() => SignificantChangeDetector.Detect(null));
        }

        [Fact]
        public void No_changes_when_diff_is_empty()
        {
            Assert.Empty(SignificantChangeDetector.Detect(EmptyDiff()));
        }

        // === Critère 1 : SC_LEVEL_DOWN ========================================

        [Fact]
        public void Detects_level_degradation_to_critical_as_critical()
        {
            var diff = EmptyDiff();
            diff.Score = new HealthScoreDelta
            {
                ValueBefore = 95, ValueAfter = 40,
                LevelBefore = HealthLevel.Excellent, LevelAfter = HealthLevel.Critical
            };

            var result = SignificantChangeDetector.Detect(diff);

            var change = Assert.Single(result);
            Assert.Equal("SC_LEVEL_DOWN", change.Code);
            Assert.Equal(IssueSeverity.Critical, change.Severity);
        }

        [Fact]
        public void Detects_level_degradation_to_warning_as_warning()
        {
            var diff = EmptyDiff();
            diff.Score = new HealthScoreDelta
            {
                ValueBefore = 95, ValueAfter = 75,
                LevelBefore = HealthLevel.Excellent, LevelAfter = HealthLevel.Warning
            };

            var result = SignificantChangeDetector.Detect(diff);

            var change = Assert.Single(result);
            Assert.Equal("SC_LEVEL_DOWN", change.Code);
            Assert.Equal(IssueSeverity.Warning, change.Severity);
        }

        [Fact]
        public void Does_not_alert_on_level_improvement()
        {
            var diff = EmptyDiff();
            diff.Score = new HealthScoreDelta
            {
                ValueBefore = 40, ValueAfter = 95,
                LevelBefore = HealthLevel.Critical, LevelAfter = HealthLevel.Excellent
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        [Fact]
        public void Does_not_alert_on_value_drop_within_same_level()
        {
            // Score baisse mais reste dans le palier Excellent. Pas d'alerte.
            var diff = EmptyDiff();
            diff.Score = new HealthScoreDelta
            {
                ValueBefore = 99, ValueAfter = 91,
                LevelBefore = HealthLevel.Excellent, LevelAfter = HealthLevel.Excellent
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        [Fact]
        public void Does_not_alert_when_score_delta_is_null()
        {
            var diff = EmptyDiff();
            diff.Score = null;

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        // === Critère 2 : SC_NEW_CRITICAL_ISSUE ================================

        [Fact]
        public void Detects_new_critical_issue()
        {
            var diff = EmptyDiff();
            diff.Issues = new SnapshotDelta<DetectedIssue>
            {
                Added = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical) }
            };

            var result = SignificantChangeDetector.Detect(diff);

            Assert.Contains(result, c => c.Code == "SC_NEW_CRITICAL_ISSUE");
            Assert.Equal(IssueSeverity.Critical, result[0].Severity);
        }

        [Fact]
        public void Ignores_new_warning_or_info_issues()
        {
            var diff = EmptyDiff();
            diff.Issues = new SnapshotDelta<DetectedIssue>
            {
                Added = new[]
                {
                    Issue("SITE_NO_BRIDGEHEAD", IssueSeverity.Warning),
                    Issue("OS_UNKNOWN", IssueSeverity.Info)
                }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        // === Critère 3 : SC_DC_LOST ============================================

        [Fact]
        public void Detects_dc_lost_as_critical()
        {
            var diff = EmptyDiff();
            diff.DomainControllers = new SnapshotDelta<DomainControllerInfo>
            {
                Removed = new[] { Dc("DC02") }
            };

            var result = SignificantChangeDetector.Detect(diff);

            var change = Assert.Single(result);
            Assert.Equal("SC_DC_LOST", change.Code);
            Assert.Equal(IssueSeverity.Critical, change.Severity);
            Assert.Contains("DC02", change.AffectedItems);
        }

        [Fact]
        public void Does_not_alert_on_dc_added()
        {
            var diff = EmptyDiff();
            diff.DomainControllers = new SnapshotDelta<DomainControllerInfo>
            {
                Added = new[] { Dc("DC03") }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        // === Critère 4 : SC_NEW_FAILURE ========================================

        [Fact]
        public void Detects_new_failure_as_warning()
        {
            var diff = EmptyDiff();
            diff.ReplicationFailures = new SnapshotDelta<ReplicationFailureInfo>
            {
                Added = new[] { Failure("DC02", "DC01") }
            };

            var result = SignificantChangeDetector.Detect(diff);

            var change = Assert.Single(result);
            Assert.Equal("SC_NEW_FAILURE", change.Code);
            Assert.Equal(IssueSeverity.Warning, change.Severity);
        }

        [Fact]
        public void Does_not_alert_on_resolved_failure()
        {
            var diff = EmptyDiff();
            diff.ReplicationFailures = new SnapshotDelta<ReplicationFailureInfo>
            {
                Removed = new[] { Failure("DC02", "DC01") }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        // === Critère 5 : SC_SRV_CRITICAL_LOST ==================================

        [Fact]
        public void Detects_srv_ldap_removed()
        {
            var diff = EmptyDiff();
            diff.DnsChecks = new SnapshotDelta<DnsCheckResult>
            {
                Removed = new[]
                {
                    Dns("_ldap._tcp.dc._msdcs.exemple.local", DnsCheckedRecordType.SrvLdap, DnsCheckStatus.Ok)
                }
            };

            var result = SignificantChangeDetector.Detect(diff);

            var change = Assert.Single(result);
            Assert.Equal("SC_SRV_CRITICAL_LOST", change.Code);
            Assert.Equal(IssueSeverity.Critical, change.Severity);
        }

        [Fact]
        public void Detects_srv_kerberos_status_change_ok_to_missing()
        {
            var before = Dns("_kerberos._tcp.exemple.local", DnsCheckedRecordType.SrvKerberos, DnsCheckStatus.Ok);
            var after = Dns("_kerberos._tcp.exemple.local", DnsCheckedRecordType.SrvKerberos, DnsCheckStatus.Missing);

            var diff = EmptyDiff();
            diff.DnsChecks = new SnapshotDelta<DnsCheckResult>
            {
                Changed = new[] { new DeltaChange<DnsCheckResult> { Before = before, After = after, Key = "k" } }
            };

            var result = SignificantChangeDetector.Detect(diff);

            Assert.Single(result);
            Assert.Equal("SC_SRV_CRITICAL_LOST", result[0].Code);
        }

        [Fact]
        public void Does_not_alert_on_srv_gc_status_change()
        {
            // Le SRV _gc est important mais classé "optionnel" : pas dans l'ensemble
            // critique. Sa perte est signalée par les détecteurs d'issues classiques,
            // pas par une alerte de monitoring temps réel.
            var before = Dns("_gc._tcp.exemple.local", DnsCheckedRecordType.SrvGc, DnsCheckStatus.Ok);
            var after = Dns("_gc._tcp.exemple.local", DnsCheckedRecordType.SrvGc, DnsCheckStatus.Missing);

            var diff = EmptyDiff();
            diff.DnsChecks = new SnapshotDelta<DnsCheckResult>
            {
                Changed = new[] { new DeltaChange<DnsCheckResult> { Before = before, After = after, Key = "k" } }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        [Fact]
        public void Does_not_alert_on_srv_recovery_from_missing_to_ok()
        {
            var before = Dns("_ldap._tcp.dc._msdcs.exemple.local", DnsCheckedRecordType.SrvLdap, DnsCheckStatus.Missing);
            var after = Dns("_ldap._tcp.dc._msdcs.exemple.local", DnsCheckedRecordType.SrvLdap, DnsCheckStatus.Ok);

            var diff = EmptyDiff();
            diff.DnsChecks = new SnapshotDelta<DnsCheckResult>
            {
                Changed = new[] { new DeltaChange<DnsCheckResult> { Before = before, After = after, Key = "k" } }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        // === Critère 6 : SC_PORT_CRITICAL_DOWN ================================

        [Fact]
        public void Detects_port_389_open_to_closed()
        {
            var before = Port(389, PortCheckStatus.Open);
            var after = Port(389, PortCheckStatus.Closed);

            var diff = EmptyDiff();
            diff.PortChecks = new SnapshotDelta<PortCheckResult>
            {
                Changed = new[] { new DeltaChange<PortCheckResult> { Before = before, After = after, Key = "k" } }
            };

            var result = SignificantChangeDetector.Detect(diff);

            var change = Assert.Single(result);
            Assert.Equal("SC_PORT_CRITICAL_DOWN", change.Code);
            Assert.Equal(IssueSeverity.Critical, change.Severity);
        }

        [Fact]
        public void Detects_port_88_open_to_timeout()
        {
            var before = Port(88, PortCheckStatus.Open);
            var after = Port(88, PortCheckStatus.Timeout);

            var diff = EmptyDiff();
            diff.PortChecks = new SnapshotDelta<PortCheckResult>
            {
                Changed = new[] { new DeltaChange<PortCheckResult> { Before = before, After = after, Key = "k" } }
            };

            Assert.Single(SignificantChangeDetector.Detect(diff));
        }

        [Fact]
        public void Does_not_alert_on_non_critical_port_change()
        {
            // Port 636 (LDAPS) n'est pas dans la liste critique : Critique mais
            // non bloquant pour le fonctionnement de l'AD au sens minimal.
            var before = Port(636, PortCheckStatus.Open);
            var after = Port(636, PortCheckStatus.Closed);

            var diff = EmptyDiff();
            diff.PortChecks = new SnapshotDelta<PortCheckResult>
            {
                Changed = new[] { new DeltaChange<PortCheckResult> { Before = before, After = after, Key = "k" } }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        [Fact]
        public void Does_not_alert_on_port_recovery()
        {
            var before = Port(389, PortCheckStatus.Closed);
            var after = Port(389, PortCheckStatus.Open);

            var diff = EmptyDiff();
            diff.PortChecks = new SnapshotDelta<PortCheckResult>
            {
                Changed = new[] { new DeltaChange<PortCheckResult> { Before = before, After = after, Key = "k" } }
            };

            Assert.Empty(SignificantChangeDetector.Detect(diff));
        }

        // === Tri final =========================================================

        [Fact]
        public void Critical_changes_come_before_warning_changes()
        {
            // Trois alertes mélangées : 1 critical (DC perdu), 1 warning (nouveau failure),
            // 1 critical (issue nouvelle). Toutes doivent ressortir, criticals d'abord.
            var diff = EmptyDiff();
            diff.DomainControllers = new SnapshotDelta<DomainControllerInfo>
            {
                Removed = new[] { Dc("DC02") }
            };
            diff.ReplicationFailures = new SnapshotDelta<ReplicationFailureInfo>
            {
                Added = new[] { Failure("DC02", "DC01") }
            };
            diff.Issues = new SnapshotDelta<DetectedIssue>
            {
                Added = new[] { Issue("DC_ISOLATED", IssueSeverity.Critical) }
            };

            var result = SignificantChangeDetector.Detect(diff);

            Assert.Equal(3, result.Count);
            Assert.Equal(IssueSeverity.Critical, result[0].Severity);
            Assert.Equal(IssueSeverity.Critical, result[1].Severity);
            Assert.Equal(IssueSeverity.Warning, result[2].Severity);
        }
    }
}
