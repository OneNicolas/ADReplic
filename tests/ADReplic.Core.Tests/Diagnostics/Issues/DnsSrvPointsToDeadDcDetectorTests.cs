using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// Détection croisée DNS/Ports : SRV pointant vers un hôte dont TOUS les ports
    /// critiques (389, 88) sont inaccessibles. Silencieux si l'une des deux sondes
    /// n'a pas été exécutée.
    /// </summary>
    public class DnsSrvPointsToDeadDcDetectorTests
    {
        // -- Helpers --

        private static DnsCheckResult Srv(string recordName, string target,
            DnsCheckedRecordType type = DnsCheckedRecordType.SrvLdap,
            DnsCheckStatus status = DnsCheckStatus.Ok)
            => new DnsCheckResult { RecordName = recordName, Type = type, Target = target, Status = status };

        private static DnsCheckResult ARecord(string host)
            => new DnsCheckResult { RecordName = host, Type = DnsCheckedRecordType.ARecord, Target = host, Status = DnsCheckStatus.Ok };

        private static PortCheckResult Port(string host, int port, PortCheckStatus status)
            => new PortCheckResult { HostName = host, Port = port, Status = status };

        private static AuditSnapshot Snapshot(DnsCheckResult[] dns, PortCheckResult[] ports)
            => new AuditSnapshot
            {
                DnsHealth = dns == null ? null : new DnsHealthResult { Checks = dns },
                PortHealth = ports == null ? null : new PortHealthResult { Checks = ports }
            };

        private static PortCheckResult[] DeadCriticalPorts(string host) => new[]
        {
            Port(host, 389, PortCheckStatus.Timeout),
            Port(host, 88,  PortCheckStatus.Timeout)
        };

        // -- Silences par construction --

        [Fact]
        public void Silent_when_snapshot_is_null()
        {
            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(null));
        }

        [Fact]
        public void Silent_when_dns_health_is_null()
        {
            var snap = Snapshot(null, DeadCriticalPorts("dc02.exemple.local"));
            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        [Fact]
        public void Silent_when_port_health_is_null()
        {
            var snap = Snapshot(new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc02.exemple.local") }, null);
            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        // -- Cas nominal --

        [Fact]
        public void Emits_warning_when_srv_points_to_host_with_all_critical_ports_dead()
        {
            var snap = Snapshot(
                new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc02.exemple.local") },
                DeadCriticalPorts("dc02.exemple.local"));

            var issue = Assert.Single(new DnsSrvPointsToDeadDcDetector().Detect(snap));
            Assert.Equal("DNS_SRV_POINTS_TO_DEAD_DC", issue.Code);
            Assert.Equal(IssueSeverity.Warning, issue.Severity);
            Assert.Contains("dc02.exemple.local", issue.Title);
            Assert.Contains("_ldap._tcp.dc._msdcs.exemple.local", issue.AffectedItems);
        }

        [Fact]
        public void Groups_multiple_srv_records_of_same_dead_host_in_one_issue()
        {
            var snap = Snapshot(
                new[]
                {
                    Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc02.exemple.local", DnsCheckedRecordType.SrvLdap),
                    Srv("_kerberos._tcp.exemple.local", "dc02.exemple.local", DnsCheckedRecordType.SrvKerberos),
                    Srv("_gc._tcp.exemple.local", "dc02.exemple.local", DnsCheckedRecordType.SrvGc)
                },
                DeadCriticalPorts("dc02.exemple.local"));

            var issue = Assert.Single(new DnsSrvPointsToDeadDcDetector().Detect(snap));
            Assert.Equal(3, issue.AffectedItems.Count);
        }

        [Fact]
        public void Emits_one_issue_per_dead_host()
        {
            var snap = Snapshot(
                new[]
                {
                    Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc02.exemple.local"),
                    Srv("_kerberos._tcp.exemple.local", "dc03.exemple.local", DnsCheckedRecordType.SrvKerberos)
                },
                DeadCriticalPorts("dc02.exemple.local")
                    .Concat(DeadCriticalPorts("dc03.exemple.local"))
                    .ToArray());

            Assert.Equal(2, new DnsSrvPointsToDeadDcDetector().Detect(snap).Count());
        }

        // -- Garde-fous "hôte réellement mort" --

        [Fact]
        public void Silent_when_only_one_critical_port_is_dead()
        {
            // Un seul port fermé = probablement un pare-feu, pas un hôte mort.
            // C'est le périmètre de PORT_CRITICAL_CLOSED, pas de ce détecteur.
            var snap = Snapshot(
                new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc02.exemple.local") },
                new[]
                {
                    Port("dc02.exemple.local", 389, PortCheckStatus.Closed),
                    Port("dc02.exemple.local", 88,  PortCheckStatus.Open)
                });

            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        [Fact]
        public void Silent_when_target_host_has_open_critical_ports()
        {
            var snap = Snapshot(
                new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc01.exemple.local") },
                new[]
                {
                    Port("dc01.exemple.local", 389, PortCheckStatus.Open),
                    Port("dc01.exemple.local", 88,  PortCheckStatus.Open)
                });

            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        [Fact]
        public void Silent_when_srv_target_has_no_critical_port_checks()
        {
            // Cible SRV hors inventaire (ports non testés) : on ne peut rien affirmer.
            var snap = Snapshot(
                new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", "dc-fantome.exemple.local") },
                new[] { Port("dc01.exemple.local", 389, PortCheckStatus.Open) });

            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        // -- Filtres de type et de statut --

        [Fact]
        public void Ignores_a_records_even_on_dead_host()
        {
            var snap = Snapshot(
                new[] { ARecord("dc02.exemple.local") },
                DeadCriticalPorts("dc02.exemple.local"));

            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        [Fact]
        public void Ignores_srv_checks_that_are_not_ok()
        {
            // Un SRV Missing/Error n'a pas de cible exploitable : couvert par les
            // détecteurs DNS_SRV_*_MISSING et DNS_RESOLUTION_ERROR.
            var snap = Snapshot(
                new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", null, status: DnsCheckStatus.Missing) },
                DeadCriticalPorts("dc02.exemple.local"));

            Assert.Empty(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }

        // -- Normalisation des noms --

        [Fact]
        public void Matches_srv_target_with_trailing_dot_and_different_case()
        {
            // Les cibles SRV issues du DNS portent souvent un point final (FQDN absolu).
            var snap = Snapshot(
                new[] { Srv("_ldap._tcp.dc._msdcs.exemple.local", "DC02.Exemple.Local.") },
                DeadCriticalPorts("dc02.exemple.local"));

            Assert.Single(new DnsSrvPointsToDeadDcDetector().Detect(snap));
        }
    }
}
