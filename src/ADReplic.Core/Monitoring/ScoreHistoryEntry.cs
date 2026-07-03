using System;
using ADReplic.Core.Models;

namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Capture compacte d'un score à un instant donné. Pensée pour alimenter
    /// un mini-graphique d'évolution dans la GUI sans avoir à retenir un
    /// AuditSnapshot complet par passe.
    /// </summary>
    public sealed class ScoreHistoryEntry
    {
        /// <summary>Horodatage local de la capture (utilisé pour l'affichage).</summary>
        public DateTime CapturedAt { get; set; }

        /// <summary>Score global 0-100.</summary>
        public int OverallScore { get; set; }

        public HealthLevel Level { get; set; }

        public int ReplicationScore { get; set; }

        /// <summary>Null si la sonde DNS n'était pas active.</summary>
        public int? DnsScore { get; set; }

        /// <summary>Null si la sonde Ports n'était pas active.</summary>
        public int? PortScore { get; set; }

        /// <summary>
        /// Construit une entrée d'historique à partir d'un score calculé.
        /// CapturedAt prend le moment courant local.
        /// </summary>
        public static ScoreHistoryEntry From(HealthScore score)
        {
            if (score == null) throw new ArgumentNullException(nameof(score));
            return new ScoreHistoryEntry
            {
                CapturedAt = DateTime.Now,
                OverallScore = score.Value,
                Level = score.Level,
                ReplicationScore = score.ReplicationScore,
                DnsScore = score.DnsScore,
                PortScore = score.PortScore
            };
        }
    }
}
