using System.Linq;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    /// <summary>
    /// Le score expliqué (v0.9.0) decompose le global en points perdus par categorie,
    /// avec les poids EFFECTIFS (renormalises quand une sonde manque), pour que la
    /// somme affichee corresponde a ce que l'utilisateur voit sur le badge.
    /// </summary>
    public class HealthScoreExplainerTests
    {
        private static HealthScore Score(int replication, int? dns, int? ports, int value)
            => new HealthScore
            {
                Value = value,
                ReplicationScore = replication,
                DnsScore = dns,
                PortScore = ports,
                ReplicationAnomalies = new[] { "1 lien(s) en échec ou injoignable(s)" },
                DnsAnomalies = new string[0],
                PortAnomalies = new[] { "1 port(s) critique(s) inaccessible(s) (LDAP/Kerberos/SMB)" }
            };

        [Fact]
        public void Null_score_yields_null()
        {
            Assert.Null(HealthScoreExplainer.Explain(null));
        }

        [Fact]
        public void Three_categories_with_nominal_weights()
        {
            var breakdown = HealthScoreExplainer.Explain(Score(75, 100, 75, 81));

            Assert.Equal(3, breakdown.Categories.Count);
            Assert.Equal(new[] { "Réplication", "DNS", "Réseau" }, breakdown.Categories.Select(c => c.Name));
            Assert.Equal(new[] { 50, 25, 25 }, breakdown.Categories.Select(c => c.WeightPercent));
            Assert.Equal(19, breakdown.TotalPointsLost);
        }

        [Fact]
        public void Points_lost_are_sub_score_gap_times_effective_weight()
        {
            var breakdown = HealthScoreExplainer.Explain(Score(75, 100, 75, 81));

            Assert.Equal(12.5, breakdown.Categories[0].PointsLost);
            Assert.Equal(0, breakdown.Categories[1].PointsLost);
            Assert.Equal(6.25, breakdown.Categories[2].PointsLost);
        }

        [Fact]
        public void Missing_probe_is_marked_and_weights_are_renormalized()
        {
            var breakdown = HealthScoreExplainer.Explain(Score(80, null, 100, 87));

            var dns = breakdown.Categories[1];
            Assert.False(dns.IsMeasured);
            Assert.Null(dns.SubScore);
            Assert.Equal(0, dns.WeightPercent);
            Assert.Equal(0, dns.PointsLost);

            // 0,5 / 0,75 = 66,7 % et 0,25 / 0,75 = 33,3 %
            Assert.Equal(67, breakdown.Categories[0].WeightPercent);
            Assert.Equal(33, breakdown.Categories[2].WeightPercent);
            Assert.Equal(20 * 0.5 / 0.75, breakdown.Categories[0].PointsLost, 3);
        }

        [Fact]
        public void Replication_alone_weighs_one_hundred_percent()
        {
            var breakdown = HealthScoreExplainer.Explain(Score(70, null, null, 70));

            Assert.Equal(100, breakdown.Categories[0].WeightPercent);
            Assert.Equal(30, breakdown.Categories[0].PointsLost);
        }

        [Fact]
        public void Category_anomalies_are_carried_over()
        {
            var breakdown = HealthScoreExplainer.Explain(Score(75, 100, 75, 81));

            Assert.Single(breakdown.Categories[0].Anomalies);
            Assert.Empty(breakdown.Categories[1].Anomalies);
            Assert.Contains("port(s) critique(s)", breakdown.Categories[2].Anomalies[0]);
        }

        [Fact]
        public void Calculator_splits_anomalies_by_category()
        {
            var snapshot = AuditSnapshotBuilder.Build(
                "exemple.local",
                new DomainControllerInfo[0],
                new[] { new ReplicationLink { Status = ReplicationLinkStatus.Failing } },
                topology: null,
                failures: new ReplicationFailureInfo[0],
                dnsHealth: new DnsHealthResult { Checks = new[] { new DnsCheckResult { Type = DnsCheckedRecordType.SrvGc, Status = DnsCheckStatus.Missing } } },
                portHealth: new PortHealthResult { Checks = new[] { new PortCheckResult { HostName = "dc01", Port = 389, Status = PortCheckStatus.Closed } } });

            var score = snapshot.HealthScore;

            Assert.Single(score.ReplicationAnomalies);
            Assert.Single(score.DnsAnomalies);
            Assert.Single(score.PortAnomalies);
            // La liste plate historique reste la concatenation des trois.
            Assert.Equal(3, score.Anomalies.Count);
        }
    }
}
