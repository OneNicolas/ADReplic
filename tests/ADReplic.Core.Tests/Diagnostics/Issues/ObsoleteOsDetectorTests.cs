using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class ObsoleteOsDetectorTests
    {
        private static DomainControllerInfo Dc(string name, string os) =>
            new DomainControllerInfo { HostName = name, OSVersion = os };

        private static AuditSnapshot Snapshot(params DomainControllerInfo[] dcs) =>
            new AuditSnapshot { DomainControllers = dcs };

        [Fact]
        public void Returns_nothing_on_null_snapshot()
        {
            Assert.Empty(new ObsoleteOsDetector().Detect(null).ToList());
        }

        [Fact]
        public void Returns_nothing_when_all_dcs_recent()
        {
            var snap = Snapshot(
                Dc("DC01", "Windows Server 2022 Standard"),
                Dc("DC02", "Windows Server 2019 Datacenter"));

            Assert.Empty(new ObsoleteOsDetector().Detect(snap).ToList());
        }

        [Fact]
        public void Flags_critical_on_server_2008()
        {
            var snap = Snapshot(Dc("DC01", "Windows Server 2008 R2 Enterprise"));

            var issue = new ObsoleteOsDetector().Detect(snap).Single();
            Assert.Equal("OS_UNSUPPORTED",        issue.Code);
            Assert.Equal(IssueSeverity.Critical,  issue.Severity);
            Assert.Contains("DC01",               issue.AffectedItems);
        }

        [Fact]
        public void Flags_warning_on_server_2012()
        {
            var snap = Snapshot(Dc("DC01", "Windows Server 2012 R2 Standard"));

            var issue = new ObsoleteOsDetector().Detect(snap).Single();
            Assert.Equal("OS_OUT_OF_EXTENDED_SUPPORT", issue.Code);
            Assert.Equal(IssueSeverity.Warning,        issue.Severity);
        }

        [Fact]
        public void Flags_info_on_unknown_os_string()
        {
            var snap = Snapshot(Dc("DC01", "Linux"));

            var issue = new ObsoleteOsDetector().Detect(snap).Single();
            Assert.Equal("OS_UNKNOWN",         issue.Code);
            Assert.Equal(IssueSeverity.Info,   issue.Severity);
        }

        [Fact]
        public void Mixed_inventory_returns_one_issue_per_obsolete_dc()
        {
            var snap = Snapshot(
                Dc("DC01", "Windows Server 2022 Standard"),    // OK
                Dc("DC02", "Windows Server 2012 R2 Standard"), // Warning
                Dc("DC03", "Windows Server 2008 R2 Standard"), // Critical
                Dc("DC04", ""));                               // Info

            var issues = new ObsoleteOsDetector().Detect(snap).ToList();

            Assert.Equal(3, issues.Count);
            Assert.Contains(issues, i => i.AffectedItems.Contains("DC02") && i.Severity == IssueSeverity.Warning);
            Assert.Contains(issues, i => i.AffectedItems.Contains("DC03") && i.Severity == IssueSeverity.Critical);
            Assert.Contains(issues, i => i.AffectedItems.Contains("DC04") && i.Severity == IssueSeverity.Info);
        }
    }
}
