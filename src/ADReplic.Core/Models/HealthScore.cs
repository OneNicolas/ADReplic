using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Score de santé global de l'infrastructure de réplication.
    /// Calculé à partir de l'AuditSnapshot par HealthScoreCalculator.
    /// </summary>
    public sealed class HealthScore
    {
        /// <summary>Score 0-100. 100 = aucune anomalie détectée.</summary>
        public int Value { get; set; }

        /// <summary>Niveau de gravité agrégé.</summary>
        public HealthLevel Level { get; set; }

        /// <summary>Libellé court ("Excellent", "Avertissements", "Critique").</summary>
        public string Label { get; set; }

        /// <summary>Phrase explicative concise (ex : "2 liens en échec, 1 avertissement").</summary>
        public string Summary { get; set; }

        /// <summary>Liste détaillée des anomalies détectées.</summary>
        public IReadOnlyList<string> Anomalies { get; set; } = new List<string>();
    }

    public enum HealthLevel
    {
        Excellent,   // 90-100 — rien à signaler
        Warning,     // 60-89  — quelques anomalies
        Critical     // 0-59   — état dégradé sérieux
    }
}
