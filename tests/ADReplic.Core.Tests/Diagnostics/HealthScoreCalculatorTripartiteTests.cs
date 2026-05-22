using System;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// Tests centrés sur la composante tripartite (DNS + Ports) du score de santé
    /// et sur la renormalisation des poids quand certaines sondes sont absentes.
    /// Les tests sur la composante Réplication seule restent dans HealthScoreCalculatorTests.
    /// </summary>
    public class HealthScoreCalculatorTripartiteTests
    {
        // -- Helpers --

        private static AuditSnapshot BuildSnapshot(
            ReplicationLink[] links = null,
            ReplicationFailureInfo[] failures = null,
            DnsHealthResult dns = null,
            PortHealthResult ports = null)
        {
            return AuditSnapshotBuilder.Build(
                "exemple.local",
                new DomainControllerInfo[0],
                links ?? new ReplicationLink[0],
                topology: null,
                failures: failures ?? new ReplicationFailureInfo[0],
                isSingleDcMode: false,
                dnsHealth: dns,
                portHealth: ports);
        }

        private static DnsHealthResult Dns(params DnsCheckResult[] checks)
            => new DnsHealthResult { Checks = checks ?? new DnsCheckResult[0] };

        private static PortHealthResult Ports(params PortCheckResult[] checks)
            => new PortHealthResult { Checks = checks ?? new PortCheckResult[0] };

        private static DnsCheckResult MissingSrv(DnsCheckedRecordType type)
            => new DnsCheckResult { Type = type, Status = DnsCheckStatus.Missing };

        private static DnsCheckResult MissingA(string host)
            => new DnsCheckResult { Type = DnsCheckedRecordType.ARecord, Status = DnsCheckStatus.Missing, RecordName = host };

        private static DnsCheckResult ErrorDns(DnsCheckedRecordType type, int errorCode = 9002)
            => new DnsCheckResult { Type = type, Status = DnsCheckStatus.Error, ErrorCode = errorCode };

        private static PortCheckResult OkPort(string host, int port)
            => new PortCheckResult { HostName = host, Port = port, Status = PortCheckStatus.Open };

        private static PortCheckResult ClosedPort(string host, int port)
            => new PortCheckResult { HostName = host, Port = port, Status = PortCheckStatus.Closed };

        private static PortCheckResult TimeoutPort(string host, int port)
            => new PortCheckResult { HostName = host, Port = port, Status = PortCheckStatus.Timeout };

        // -- ReplicationScore exposé --

        [Fact]
        public void Replication_score_is_exposed_separately()
        {
            var snap = BuildSnapshot(new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Failing }
            });

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 25 = 75
            Assert.Equal(75, score.ReplicationScore);
            Assert.Equal(75, score.Value);
        }

        [Fact]
        public void Dns_and_port_scores_are_null_when_probes_not_executed()
        {
            var snap = BuildSnapshot();
            var score = HealthScoreCalculator.Compute(snap);

            Assert.Null(score.DnsScore);
            Assert.Null(score.PortScore);
        }

        // -- Renormalisation des poids --

        [Fact]
        public void Score_equals_replication_when_dns_and_ports_absent()
        {
            // Sans aucune sonde DNS/Ports, le poids global doit être 100% sur Replication.
            // Cas vital pour préserver le comportement v0.3.0 dans la GUI actuelle.
            var snap = BuildSnapshot(new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                new ReplicationLink { Status = ReplicationLinkStatus.Failing }
            });

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 2*25 = 50, et global = repliScore (totalWeight = 0.5, renormalisation 1.0)
            Assert.Equal(50, score.ReplicationScore);
            Assert.Equal(50, score.Value);
        }

        [Fact]
        public void All_perfect_subscores_yield_100_global()
        {
            var snap = BuildSnapshot(
                dns: Dns(),    // checks vides = aucune anomalie = 100
                ports: Ports());

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(100, score.ReplicationScore);
            Assert.Equal(100, score.DnsScore);
            Assert.Equal(100, score.PortScore);
            Assert.Equal(100, score.Value);
        }

        [Fact]
        public void Three_full_subscores_use_50_25_25_weighting()
        {
            // Repli 60, DNS 100, Ports 100 → 60*0.5 + 100*0.25 + 100*0.25 = 80
            var snap = BuildSnapshot(
                links: new[]
                {
                    // 100 - 8*5 = 60
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                    new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                },
                dns: Dns(),
                ports: Ports());

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(60, score.ReplicationScore);
            Assert.Equal(80, score.Value);
        }

        [Fact]
        public void Renormalizes_when_only_dns_is_present()
        {
            // Repli 50, DNS 100, pas de Ports → renormalisé sur (0.5+0.25) = 0.75
            // global = (50*0.5 + 100*0.25) / 0.75 = 50/0.75 = 66.67 → 67
            var snap = BuildSnapshot(
                links: new[]
                {
                    // 100 - 2*25 = 50
                    new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                    new ReplicationLink { Status = ReplicationLinkStatus.Failing }
                },
                dns: Dns());

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(50, score.ReplicationScore);
            Assert.Equal(100, score.DnsScore);
            Assert.Null(score.PortScore);
            Assert.Equal(67, score.Value);
        }

        [Fact]
        public void Renormalizes_when_only_ports_is_present()
        {
            // Symétrique du test DNS.
            var snap = BuildSnapshot(
                links: new[]
                {
                    new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                    new ReplicationLink { Status = ReplicationLinkStatus.Failing }
                },
                ports: Ports());

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(50, score.ReplicationScore);
            Assert.Null(score.DnsScore);
            Assert.Equal(100, score.PortScore);
            Assert.Equal(67, score.Value);
        }

        // -- Sous-score DNS --

        [Fact]
        public void Dns_missing_critical_srv_costs_25_points_each()
        {
            var snap = BuildSnapshot(dns: Dns(
                MissingSrv(DnsCheckedRecordType.SrvLdap),
                MissingSrv(DnsCheckedRecordType.SrvKerberos)));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 2*25 = 50
            Assert.Equal(50, score.DnsScore);
            Assert.Contains(score.Anomalies, a => a.Contains("SRV critique"));
        }

        [Fact]
        public void Dns_missing_optional_srv_costs_10_points_each()
        {
            var snap = BuildSnapshot(dns: Dns(
                MissingSrv(DnsCheckedRecordType.SrvGc),
                MissingSrv(DnsCheckedRecordType.SrvKpasswd)));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 2*10 = 80
            Assert.Equal(80, score.DnsScore);
        }

        [Fact]
        public void Dns_missing_a_record_for_dc_costs_20_points()
        {
            var snap = BuildSnapshot(dns: Dns(
                MissingA("dc01.exemple.local")));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 20 = 80
            Assert.Equal(80, score.DnsScore);
            Assert.Contains(score.Anomalies, a => a.Contains("DC sans enregistrement A"));
        }

        [Fact]
        public void Dns_error_costs_15_points()
        {
            var snap = BuildSnapshot(dns: Dns(
                ErrorDns(DnsCheckedRecordType.SrvLdap, errorCode: 9002)));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 15 = 85
            Assert.Equal(85, score.DnsScore);
        }

        [Fact]
        public void Dns_score_floors_at_zero()
        {
            // Beaucoup d'anomalies → score plancher à 0
            var snap = BuildSnapshot(dns: Dns(
                MissingSrv(DnsCheckedRecordType.SrvLdap),
                MissingSrv(DnsCheckedRecordType.SrvKerberos),
                MissingSrv(DnsCheckedRecordType.SrvLdap),
                MissingSrv(DnsCheckedRecordType.SrvKerberos),
                MissingSrv(DnsCheckedRecordType.SrvLdap)));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 5*25 = -25 → 0
            Assert.Equal(0, score.DnsScore);
        }

        // -- Sous-score Ports --

        [Fact]
        public void Critical_port_closed_costs_25_points()
        {
            var snap = BuildSnapshot(ports: Ports(
                ClosedPort("dc01.exemple.local", 389)));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 25 = 75
            Assert.Equal(75, score.PortScore);
            Assert.Contains(score.Anomalies, a => a.Contains("port(s) critique"));
        }

        [Fact]
        public void Critical_port_timeout_treated_as_blocked()
        {
            // 389 en timeout = aussi grave que 389 fermé : -25
            var snap = BuildSnapshot(ports: Ports(
                TimeoutPort("dc01.exemple.local", 389)));

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(75, score.PortScore);
        }

        [Fact]
        public void Non_critical_port_closed_costs_only_5_points()
        {
            var snap = BuildSnapshot(ports: Ports(
                ClosedPort("dc01.exemple.local", 3268))); // GC

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 5 = 95
            Assert.Equal(95, score.PortScore);
        }

        [Fact]
        public void Critical_ports_are_389_88_445()
        {
            // Chaque port critique fermé coûte 25, les autres seulement 5.
            var snap = BuildSnapshot(ports: Ports(
                ClosedPort("dc01.exemple.local", 389),
                ClosedPort("dc01.exemple.local", 88),
                ClosedPort("dc01.exemple.local", 445),
                ClosedPort("dc01.exemple.local", 636) // pas critique
            ));

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 3*25 - 5 = 20
            Assert.Equal(20, score.PortScore);
        }

        // -- Summary contient les anomalies --

        [Fact]
        public void Summary_lists_all_categories_of_anomalies()
        {
            var snap = BuildSnapshot(
                links: new[] { new ReplicationLink { Status = ReplicationLinkStatus.Failing } },
                dns: Dns(MissingSrv(DnsCheckedRecordType.SrvLdap)),
                ports: Ports(ClosedPort("dc01.exemple.local", 389)));

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Contains("lien", score.Summary);
            Assert.Contains("SRV critique", score.Summary);
            Assert.Contains("port", score.Summary);
        }

        [Fact]
        public void Empty_dns_health_yields_perfect_dns_score()
        {
            var snap = BuildSnapshot(dns: Dns()); // 0 checks
            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(100, score.DnsScore);
        }

        [Fact]
        public void Empty_port_health_yields_perfect_port_score()
        {
            var snap = BuildSnapshot(ports: Ports()); // 0 checks
            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(100, score.PortScore);
        }

        [Fact]
        public void Ok_ports_do_not_change_port_score()
        {
            var snap = BuildSnapshot(ports: Ports(
                OkPort("dc01.exemple.local", 389),
                OkPort("dc01.exemple.local", 88),
                OkPort("dc01.exemple.local", 3268)));

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(100, score.PortScore);
        }
    }
}
