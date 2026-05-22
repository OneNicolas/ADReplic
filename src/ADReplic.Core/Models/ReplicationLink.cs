using System;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Statut consolidé d'un lien de réplication entrant (source -> destination).
    /// Une ligne par (DC destination, partenaire source, contexte de nommage).
    /// </summary>
    public sealed class ReplicationLink
    {
        public string DestinationDc { get; set; }
        public string SourceDc { get; set; }
        public string NamingContext { get; set; }
        public string PartitionType { get; set; }
        public bool IsIntraSite { get; set; }

        public DateTime? LastAttempt { get; set; }
        public DateTime? LastSuccess { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int LastResultCode { get; set; }
        public string LastResultMessage { get; set; }

        public ReplicationLinkStatus Status { get; set; }

        /// <summary>Latence entre la dernière tentative et le dernier succès (null si jamais réussi).</summary>
        public TimeSpan? Latency =>
            (LastAttempt.HasValue && LastSuccess.HasValue)
                ? LastAttempt.Value - LastSuccess.Value
                : (TimeSpan?)null;
    }

    public enum ReplicationLinkStatus
    {
        Unknown = 0,
        Healthy = 1,
        Warning = 2,
        Failing = 3,
        Unreachable = 4
    }
}
