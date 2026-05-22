using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// Tests des 2 détecteurs Ports. Tous se taisent par construction quand
    /// snapshot.PortHealth est null.
    /// </summary>
    public class PortHealthDetectorsTests
    {
        // -- Helpers --

        private static AuditSnapshot WithPorts(params PortCheckResult[] checks)
        {
            return new AuditSnapshot
            {
                PortHealth = new PortHealthResult { Checks = checks ?? new PortCheckResult[0] }
            };
        }

        private static PortCheckResult Closed(string host, int port, string label = "LDAP")
            => new PortCheckResult { HostName = host, Port = port, ServiceLabel = label, Status = PortCheckStatus.Closed };

        private static PortCheckResult TimedOut(string host, int port, string label = "LDAP")
            => new PortCheckResult { HostName = host, Port = port, ServiceLabel = label, Status = PortCheckStatus.Timeout };

        private static PortCheckResult Open(string host, int port, string label = "LDAP")
            => new PortCheckResult { HostName = host, Port = port, ServiceLabel = label, Status = PortCheckStatus.Open };

        // -- PortCriticalClosedDetector --

        [Fact]
        public void Critical_silent_when_port_health_is_null()
        {
            Assert.Empty(new PortCriticalClosedDetector().Detect(new AuditSnapshot()));
        }

        [Fact]
        public void Critical_silent_when_snapshot_is_null()
        {
            Assert.Empty(new PortCriticalClosedDetector().Detect(null));
        }

        [Theory]
        [InlineData(389, "LDAP")]
        [InlineData(88, "Kerberos")]
        [InlineData(445, "SMB")]
        public void Critical_emits_when_critical_port_closed(int port, string label)
        {
            var snap = WithPorts(Closed("dc01.exemple.local", port, label));

            var issues = new PortCriticalClosedDetector().Detect(snap).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal("PORT_CRITICAL_CLOSED", issue.Code);
            Assert.Equal(IssueSeverity.Critical, issue.Severity);
            Assert.Contains($"dc01.exemple.local:{port}", issue.AffectedItems);
        }

        [Theory]
        [InlineData(389)]
        [InlineData(88)]
        [InlineData(445)]
        public void Critical_emits_when_critical_port_in_timeout(int port)
        {
            var snap = WithPorts(TimedOut("dc01.exemple.local", port));

            var issues = new PortCriticalClosedDetector().Detect(snap).ToList();

            Assert.Single(issues);
        }

        [Fact]
        public void Critical_does_not_emit_for_non_critical_port()
        {
            var snap = WithPorts(
                Closed("dc01.exemple.local", 636, "LDAPS"),
                Closed("dc01.exemple.local", 3268, "GC"));

            Assert.Empty(new PortCriticalClosedDetector().Detect(snap));
        }

        [Fact]
        public void Critical_does_not_emit_for_open_critical_port()
        {
            var snap = WithPorts(Open("dc01.exemple.local", 389, "LDAP"));

            Assert.Empty(new PortCriticalClosedDetector().Detect(snap));
        }

        [Fact]
        public void Critical_emits_one_issue_per_host_port_pair()
        {
            var snap = WithPorts(
                Closed("dc01.exemple.local", 389, "LDAP"),
                Closed("dc01.exemple.local", 88, "Kerberos"),
                Closed("dc02.exemple.local", 389, "LDAP"));

            var issues = new PortCriticalClosedDetector().Detect(snap).ToList();

            Assert.Equal(3, issues.Count);
        }

        [Fact]
        public void Critical_title_mentions_closed_vs_timeout_distinction()
        {
            var snapClosed = WithPorts(Closed("dc01.exemple.local", 389, "LDAP"));
            var snapTimeout = WithPorts(TimedOut("dc01.exemple.local", 389, "LDAP"));

            var closedIssue = new PortCriticalClosedDetector().Detect(snapClosed).Single();
            var timeoutIssue = new PortCriticalClosedDetector().Detect(snapTimeout).Single();

            // Les deux ont la même sévérité, mais la description doit refléter le statut réel
            Assert.Contains("fermé", closedIssue.Description);
            Assert.Contains("timeout", timeoutIssue.Description);
        }

        // -- PortClosedOrFilteredDetector --

        [Fact]
        public void Other_silent_when_port_health_is_null()
        {
            Assert.Empty(new PortClosedOrFilteredDetector().Detect(new AuditSnapshot()));
        }

        [Theory]
        [InlineData(53, "DNS")]
        [InlineData(135, "RPC")]
        [InlineData(464, "kpasswd")]
        [InlineData(636, "LDAPS")]
        [InlineData(3268, "GC")]
        [InlineData(3269, "GC LDAPS")]
        public void Other_emits_when_non_critical_port_closed(int port, string label)
        {
            var snap = WithPorts(Closed("dc01.exemple.local", port, label));

            var issues = new PortClosedOrFilteredDetector().Detect(snap).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal("PORT_CLOSED_OR_FILTERED", issue.Code);
            Assert.Equal(IssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void Other_does_not_emit_for_critical_port()
        {
            // Pas de double émission : les ports critiques sont du ressort de PortCriticalClosedDetector.
            var snap = WithPorts(
                Closed("dc01.exemple.local", 389, "LDAP"),
                Closed("dc01.exemple.local", 88, "Kerberos"),
                Closed("dc01.exemple.local", 445, "SMB"));

            Assert.Empty(new PortClosedOrFilteredDetector().Detect(snap));
        }

        [Fact]
        public void Other_emits_for_timeout_too()
        {
            var snap = WithPorts(TimedOut("dc01.exemple.local", 636, "LDAPS"));

            Assert.Single(new PortClosedOrFilteredDetector().Detect(snap));
        }

        [Fact]
        public void Other_does_not_emit_for_open_port()
        {
            var snap = WithPorts(Open("dc01.exemple.local", 636, "LDAPS"));

            Assert.Empty(new PortClosedOrFilteredDetector().Detect(snap));
        }

        // -- Vérifie qu'un même snapshot ne déclenche pas les deux détecteurs sur le même port --

        [Fact]
        public void Critical_and_other_detectors_are_mutually_exclusive_on_same_port()
        {
            var snap = WithPorts(
                Closed("dc01.exemple.local", 389, "LDAP"),   // critique
                Closed("dc01.exemple.local", 636, "LDAPS")); // autre

            var critical = new PortCriticalClosedDetector().Detect(snap).ToList();
            var other = new PortClosedOrFilteredDetector().Detect(snap).ToList();

            Assert.Single(critical);
            Assert.Single(other);
            // Pas de chevauchement : Total issues = nombre de ports distincts en anomalie.
            Assert.Equal(2, critical.Count + other.Count);
        }
    }
}
