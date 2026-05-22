using System;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Échec de réplication en cours, retourné par GetAllReplicationFailures().
    /// Plus ciblé qu'un ReplicationLink : ne contient que les liens en panne actuelle.
    /// </summary>
    public sealed class ReplicationFailureInfo
    {
        public string DestinationDc { get; set; }
        public string SourceDc { get; set; }
        public DateTime FirstFailureTime { get; set; }
        public int ConsecutiveFailureCount { get; set; }
        public int LastErrorCode { get; set; }
        public string LastErrorMessage { get; set; }
        public ReplicationFailureSeverity Severity { get; set; }

        public TimeSpan FailureDuration => DateTime.Now - FirstFailureTime;
    }

    public enum ReplicationFailureSeverity
    {
        Unknown = 0,
        Recent = 1,    // < 1h, échec transient probable
        Sustained = 2, // 1h - 24h, nécessite attention
        Critical = 3   // > 24h, risque de tombstone si > TSL/2
    }
}
