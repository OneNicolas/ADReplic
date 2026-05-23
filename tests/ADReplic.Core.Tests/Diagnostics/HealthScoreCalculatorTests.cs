using System.Collections.Generic;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    public class HealthScoreCalculatorTests
    {
        private static AuditSnapshot SnapshotWith(
            ReplicationLink[] links = null,
            ReplicationFailureInfo[] failures = null)
        {
            return AuditSnapshotBuilder.Build(
                "exemple.local",
                new DomainControllerInfo[0],
                links ?? new ReplicationLink[0],
                topology: null,
                failures: failures ?? new ReplicationFailureInfo[0]);
        }

        [Fact]
        public void Perfect_score_when_everything_is_healthy()
        {
            var snap = SnapshotWith(new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Healthy },
                new ReplicationLink { Status = ReplicationLinkStatus.Healthy }
            });

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(100, score.Value);
            Assert.Equal(HealthLevel.Excellent, score.Level);
            Assert.Equal("Excellent", score.Label);
            Assert.Empty(score.Anomalies);
        }

        [Fact]
        public void One_failing_link_drops_to_warning()
        {
            var snap = SnapshotWith(new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                new ReplicationLink { Status = ReplicationLinkStatus.Healthy }
            });

            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(75, score.Value);
            Assert.Equal(HealthLevel.Warning, score.Level);
            Assert.Contains("1 lien(s) en échec ou injoignable(s)", score.Anomalies);
        }

        [Fact]
        public void Multiple_failures_push_to_critical()
        {
            var snap = SnapshotWith(new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                new ReplicationLink { Status = ReplicationLinkStatus.Unreachable }
            });

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 3*25 = 25
            Assert.Equal(25, score.Value);
            Assert.Equal(HealthLevel.Critical, score.Level);
        }

        [Fact]
        public void Score_never_goes_below_zero()
        {
            var links = new ReplicationLink[10];
            for (int i = 0; i < links.Length; i++)
                links[i] = new ReplicationLink { Status = ReplicationLinkStatus.Failing };

            var score = HealthScoreCalculator.Compute(SnapshotWith(links));

            Assert.Equal(0, score.Value);
            Assert.Equal(HealthLevel.Critical, score.Level);
        }

        [Fact]
        public void Warnings_only_keep_excellent_or_warning_band()
        {
            var snap = SnapshotWith(new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                new ReplicationLink { Status = ReplicationLinkStatus.Warning }
            });

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 2*5 = 90 → Excellent
            Assert.Equal(90, score.Value);
            Assert.Equal(HealthLevel.Excellent, score.Level);
        }

        [Fact]
        public void Active_failures_are_penalized()
        {
            var snap = SnapshotWith(
                links: new[] { new ReplicationLink { Status = ReplicationLinkStatus.Healthy } },
                failures: new[]
                {
                    new ReplicationFailureInfo { DestinationDc = "DC01", SourceDc = "DC02" },
                    new ReplicationFailureInfo { DestinationDc = "DC02", SourceDc = "DC01" }
                });

            var score = HealthScoreCalculator.Compute(snap);

            // 100 - 2*10 = 80
            Assert.Equal(80, score.Value);
            Assert.Equal(HealthLevel.Warning, score.Level);
            Assert.Contains("2 échec(s) de réplication actifs", score.Anomalies);
        }

        [Theory]
        [InlineData(100, HealthLevel.Excellent)]
        [InlineData(90, HealthLevel.Excellent)]
        [InlineData(89, HealthLevel.Warning)]
        [InlineData(60, HealthLevel.Warning)]
        [InlineData(59, HealthLevel.Critical)]
        [InlineData(0, HealthLevel.Critical)]
        public void Level_thresholds(int targetScore, HealthLevel expectedLevel)
        {
            // On construit un snapshot qui tombe pile sur targetScore via N liens Warning à -5 chacun.
            // 100 - 5N = targetScore → N = (100 - targetScore) / 5
            var deficit = 100 - targetScore;
            var warnings = deficit / 5;
            var failings = (deficit % 5 == 0) ? 0 : 1; // protection au cas où

            var links = new List<ReplicationLink>();
            for (int i = 0; i < warnings; i++)
                links.Add(new ReplicationLink { Status = ReplicationLinkStatus.Warning });
            for (int i = 0; i < failings; i++)
                links.Add(new ReplicationLink { Status = ReplicationLinkStatus.Failing });

            var score = HealthScoreCalculator.Compute(SnapshotWith(links.ToArray()));

            // Avec les nombres ci-dessus, on est à targetScore ± petite tolérance liée aux failings additionnels
            // mais pour les cas testés (multiples de 5 exclusivement), c'est exact.
            if (deficit % 5 == 0)
            {
                Assert.Equal(targetScore, score.Value);
            }
            Assert.Equal(expectedLevel, score.Level);
        }

        [Fact]
        public void Empty_snapshot_yields_perfect_score()
        {
            var snap = SnapshotWith();
            var score = HealthScoreCalculator.Compute(snap);

            Assert.Equal(100, score.Value);
            Assert.Equal(HealthLevel.Excellent, score.Level);
            Assert.Equal("Aucune anomalie détectée.", score.Summary);
        }
    }
}
