using System;

namespace ADReplic.Core.Replication
{
    /// <summary>
    /// Règles métier de classification d'un lien de réplication.
    /// Isolées ici pour qu'un changement de seuils ne touche pas au code de collecte.
    /// </summary>
    public static class ReplicationStatusEvaluator
    {
        // Seuils ajustables. À terme, à externaliser dans un fichier de config.
        public static readonly TimeSpan WarningLatencyThreshold = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan FailingLatencyThreshold = TimeSpan.FromHours(3);
        public const int WarningFailureCount = 1;
        public const int FailingFailureCount = 3;

        public static Models.ReplicationLinkStatus Evaluate(
            int consecutiveFailures,
            int lastResultCode,
            DateTime? lastAttempt,
            DateTime? lastSuccess)
        {
            if (consecutiveFailures >= FailingFailureCount || lastResultCode != 0 && consecutiveFailures >= WarningFailureCount)
            {
                if (consecutiveFailures >= FailingFailureCount)
                    return Models.ReplicationLinkStatus.Failing;
            }

            if (!lastSuccess.HasValue)
                return Models.ReplicationLinkStatus.Unknown;

            if (lastAttempt.HasValue)
            {
                var latency = lastAttempt.Value - lastSuccess.Value;
                if (latency >= FailingLatencyThreshold) return Models.ReplicationLinkStatus.Failing;
                if (latency >= WarningLatencyThreshold) return Models.ReplicationLinkStatus.Warning;
            }

            if (lastResultCode != 0 || consecutiveFailures >= WarningFailureCount)
                return Models.ReplicationLinkStatus.Warning;

            return Models.ReplicationLinkStatus.Healthy;
        }
    }
}
