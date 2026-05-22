using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// Tests des 4 détecteurs DNS. Tous se taisent par construction quand
    /// snapshot.DnsHealth est null (cas Phase B sans sondes branchées GUI/PowerShell).
    /// </summary>
    public class DnsHealthDetectorsTests
    {
        // -- Helpers --

        private static AuditSnapshot WithDns(params DnsCheckResult[] checks)
        {
            return new AuditSnapshot
            {
                DnsHealth = new DnsHealthResult { Checks = checks ?? new DnsCheckResult[0] }
            };
        }

        private static DnsCheckResult Missing(DnsCheckedRecordType type, string recordName)
            => new DnsCheckResult
            {
                Type = type,
                Status = DnsCheckStatus.Missing,
                RecordName = recordName,
                ErrorCode = 9003
            };

        private static DnsCheckResult ErrorCheck(string recordName, int errorCode = 9002)
            => new DnsCheckResult
            {
                Type = DnsCheckedRecordType.SrvLdap,
                Status = DnsCheckStatus.Error,
                RecordName = recordName,
                ErrorCode = errorCode
            };

        private static DnsCheckResult OkCheck(DnsCheckedRecordType type, string recordName)
            => new DnsCheckResult
            {
                Type = type,
                Status = DnsCheckStatus.Ok,
                RecordName = recordName,
                Target = "dc01.exemple.local"
            };

        // -- DnsSrvCriticalMissingDetector --

        [Fact]
        public void Critical_missing_silent_when_dns_health_is_null()
        {
            var detector = new DnsSrvCriticalMissingDetector();
            Assert.Empty(detector.Detect(new AuditSnapshot()));
        }

        [Fact]
        public void Critical_missing_silent_when_snapshot_is_null()
        {
            var detector = new DnsSrvCriticalMissingDetector();
            Assert.Empty(detector.Detect(null));
        }

        [Fact]
        public void Critical_missing_emits_for_missing_ldap_srv()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvLdap, "_ldap._tcp.dc._msdcs.exemple.local"));

            var issues = new DnsSrvCriticalMissingDetector().Detect(snap).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal("DNS_SRV_CRITICAL_MISSING", issue.Code);
            Assert.Equal(IssueSeverity.Critical, issue.Severity);
            Assert.Contains("_ldap._tcp.dc._msdcs.exemple.local", issue.AffectedItems);
        }

        [Fact]
        public void Critical_missing_emits_for_missing_kerberos_srv()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvKerberos, "_kerberos._tcp.exemple.local"));

            var issues = new DnsSrvCriticalMissingDetector().Detect(snap).ToList();

            Assert.Single(issues);
        }

        [Fact]
        public void Critical_missing_does_not_emit_for_optional_srv()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvGc, "_gc._tcp.exemple.local"),
                Missing(DnsCheckedRecordType.SrvKpasswd, "_kpasswd._tcp.exemple.local"));

            Assert.Empty(new DnsSrvCriticalMissingDetector().Detect(snap));
        }

        [Fact]
        public void Critical_missing_does_not_emit_for_ok_srv()
        {
            var snap = WithDns(
                OkCheck(DnsCheckedRecordType.SrvLdap, "_ldap._tcp.dc._msdcs.exemple.local"));

            Assert.Empty(new DnsSrvCriticalMissingDetector().Detect(snap));
        }

        // -- DnsSrvOptionalMissingDetector --

        [Fact]
        public void Optional_missing_silent_when_dns_health_is_null()
        {
            Assert.Empty(new DnsSrvOptionalMissingDetector().Detect(new AuditSnapshot()));
        }

        [Fact]
        public void Optional_missing_emits_for_missing_gc()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvGc, "_gc._tcp.exemple.local"));

            var issues = new DnsSrvOptionalMissingDetector().Detect(snap).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal("DNS_SRV_OPTIONAL_MISSING", issue.Code);
            Assert.Equal(IssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void Optional_missing_emits_for_missing_kpasswd()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvKpasswd, "_kpasswd._tcp.exemple.local"));

            Assert.Single(new DnsSrvOptionalMissingDetector().Detect(snap));
        }

        [Fact]
        public void Optional_missing_does_not_emit_for_critical_srv()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvLdap, "_ldap._tcp.dc._msdcs.exemple.local"),
                Missing(DnsCheckedRecordType.SrvKerberos, "_kerberos._tcp.exemple.local"));

            Assert.Empty(new DnsSrvOptionalMissingDetector().Detect(snap));
        }

        // -- DnsResolutionErrorDetector --

        [Fact]
        public void Resolution_error_silent_when_dns_health_is_null()
        {
            Assert.Empty(new DnsResolutionErrorDetector().Detect(new AuditSnapshot()));
        }

        [Fact]
        public void Resolution_error_emits_for_error_status()
        {
            var snap = WithDns(ErrorCheck("_ldap._tcp.dc._msdcs.exemple.local", errorCode: 9002));

            var issues = new DnsResolutionErrorDetector().Detect(snap).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal("DNS_RESOLUTION_ERROR", issue.Code);
            Assert.Equal(IssueSeverity.Warning, issue.Severity);
            Assert.Contains("9002", issue.Description);
        }

        [Fact]
        public void Resolution_error_does_not_emit_for_missing_status()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvLdap, "_ldap._tcp.dc._msdcs.exemple.local"));

            Assert.Empty(new DnsResolutionErrorDetector().Detect(snap));
        }

        [Fact]
        public void Resolution_error_does_not_emit_for_ok_status()
        {
            var snap = WithDns(
                OkCheck(DnsCheckedRecordType.SrvLdap, "_ldap._tcp.dc._msdcs.exemple.local"));

            Assert.Empty(new DnsResolutionErrorDetector().Detect(snap));
        }

        // -- DnsDcARecordMissingDetector --

        [Fact]
        public void Dc_a_record_missing_silent_when_dns_health_is_null()
        {
            Assert.Empty(new DnsDcARecordMissingDetector().Detect(new AuditSnapshot()));
        }

        [Fact]
        public void Dc_a_record_missing_emits_for_missing_a_record()
        {
            var snap = WithDns(Missing(DnsCheckedRecordType.ARecord, "dc01.exemple.local"));

            var issues = new DnsDcARecordMissingDetector().Detect(snap).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal("DNS_DC_A_RECORD_MISSING", issue.Code);
            Assert.Equal(IssueSeverity.Critical, issue.Severity);
            Assert.Contains("dc01.exemple.local", issue.AffectedItems);
        }

        [Fact]
        public void Dc_a_record_missing_does_not_emit_for_missing_srv()
        {
            // Un SRV manquant n'est PAS un A record manquant
            var snap = WithDns(
                Missing(DnsCheckedRecordType.SrvLdap, "_ldap._tcp.dc._msdcs.exemple.local"));

            Assert.Empty(new DnsDcARecordMissingDetector().Detect(snap));
        }

        [Fact]
        public void Dc_a_record_missing_does_not_emit_for_ok_a_record()
        {
            var snap = WithDns(
                OkCheck(DnsCheckedRecordType.ARecord, "dc01.exemple.local"));

            Assert.Empty(new DnsDcARecordMissingDetector().Detect(snap));
        }

        [Fact]
        public void Dc_a_record_missing_emits_one_issue_per_missing_dc()
        {
            var snap = WithDns(
                Missing(DnsCheckedRecordType.ARecord, "dc01.exemple.local"),
                Missing(DnsCheckedRecordType.ARecord, "dc02.exemple.local"),
                Missing(DnsCheckedRecordType.ARecord, "dc03.exemple.local"));

            var issues = new DnsDcARecordMissingDetector().Detect(snap).ToList();

            Assert.Equal(3, issues.Count);
        }
    }
}
