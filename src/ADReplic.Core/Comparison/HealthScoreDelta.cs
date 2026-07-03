using ADReplic.Core.Models;

namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Évolution du score de santé entre deux audits. Conserve la valeur
    /// numérique, le niveau, et les sous-scores tripartites pour permettre
    /// à la GUI de montrer où la dégradation (ou l'amélioration) s'est produite.
    /// </summary>
    public sealed class HealthScoreDelta
    {
        public int ValueBefore { get; set; }
        public int ValueAfter { get; set; }

        /// <summary>Variation signée du score global (positif = amélioration).</summary>
        public int Delta => ValueAfter - ValueBefore;

        public HealthLevel LevelBefore { get; set; }
        public HealthLevel LevelAfter { get; set; }

        /// <summary>Vrai si le palier (Excellent/Warning/Critical) a changé.</summary>
        public bool LevelChanged => LevelBefore != LevelAfter;

        /// <summary>Vrai si le palier a empiré (Excellent -> Warning, Warning -> Critical, etc.).</summary>
        public bool LevelDegraded => (int)LevelAfter > (int)LevelBefore;

        public int ReplicationScoreBefore { get; set; }
        public int ReplicationScoreAfter { get; set; }

        /// <summary>Sous-score DNS avant. Null si la sonde n'a pas été exécutée pour la référence.</summary>
        public int? DnsScoreBefore { get; set; }

        /// <summary>Sous-score DNS après. Null si la sonde n'a pas été exécutée pour l'audit courant.</summary>
        public int? DnsScoreAfter { get; set; }

        public int? PortScoreBefore { get; set; }
        public int? PortScoreAfter { get; set; }
    }
}
