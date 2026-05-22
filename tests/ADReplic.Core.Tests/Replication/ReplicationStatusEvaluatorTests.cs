using System;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;
using Xunit;

namespace ADReplic.Core.Tests.Replication
{
    /// <summary>
    /// Tests des règles de classification du statut d'un lien de réplication.
    /// Ces règles définissent ce que l'outil considère comme sain, en avertissement
    /// ou en échec — tout changement ici a un impact direct sur les rapports clients.
    /// </summary>
    public class ReplicationStatusEvaluatorTests
    {
        private static readonly DateTime Now = new DateTime(2026, 5, 21, 10, 0, 0);

        [Fact]
        public void Healthy_when_recent_success_and_no_failures()
        {
            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: 0,
                lastResultCode: 0,
                lastAttempt: Now,
                lastSuccess: Now);

            Assert.Equal(ReplicationLinkStatus.Healthy, status);
        }

        [Fact]
        public void Unknown_when_never_replicated_and_no_failures()
        {
            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: 0,
                lastResultCode: 0,
                lastAttempt: null,
                lastSuccess: null);

            Assert.Equal(ReplicationLinkStatus.Unknown, status);
        }

        [Fact]
        public void Warning_when_latency_above_30_minutes()
        {
            var attempt = Now;
            var success = Now.AddMinutes(-31);

            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: 0,
                lastResultCode: 0,
                lastAttempt: attempt,
                lastSuccess: success);

            Assert.Equal(ReplicationLinkStatus.Warning, status);
        }

        [Fact]
        public void Failing_when_latency_above_3_hours()
        {
            var attempt = Now;
            var success = Now.AddHours(-4);

            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: 0,
                lastResultCode: 0,
                lastAttempt: attempt,
                lastSuccess: success);

            Assert.Equal(ReplicationLinkStatus.Failing, status);
        }

        [Fact]
        public void Failing_when_three_or_more_consecutive_failures()
        {
            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: 3,
                lastResultCode: 1722,
                lastAttempt: Now,
                lastSuccess: Now.AddMinutes(-5));

            Assert.Equal(ReplicationLinkStatus.Failing, status);
        }

        [Fact]
        public void Warning_when_one_failure_but_recent_success()
        {
            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: 1,
                lastResultCode: 1722,
                lastAttempt: Now,
                lastSuccess: Now.AddMinutes(-5));

            Assert.Equal(ReplicationLinkStatus.Warning, status);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 1)]
        public void Healthy_below_all_thresholds(int failures, int latencyMinutes)
        {
            var status = ReplicationStatusEvaluator.Evaluate(
                consecutiveFailures: failures,
                lastResultCode: 0,
                lastAttempt: Now,
                lastSuccess: Now.AddMinutes(-latencyMinutes));

            Assert.Equal(ReplicationLinkStatus.Healthy, status);
        }
    }
}
