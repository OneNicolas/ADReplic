using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Score de santé global de l'infrastructure de réplication.
    /// Calculé à partir de l'AuditSnapshot par HealthScoreCalculator.
    /// </summary>
    public sealed class HealthScore
    {
        /// <summary>Score global 0-100 pondéré. 100 = aucune anomalie détectée.</summary>
        public int Value { get; set; }

        /// <summary>Niveau de gravité agrégé.</summary>
        public HealthLevel Level { get; set; }

        /// <summary>Libellé court ("Excellent", "Avertissements", "Critique").</summary>
        public string Label { get; set; }

        /// <summary>Phrase explicative concise (ex : "2 liens en échec, 1 avertissement").</summary>
        public string Summary { get; set; }

        /// <summary>Liste détaillée des anomalies détectées, toutes catégories confondues.</summary>
        public IReadOnlyList<string> Anomalies { get; set; } = new List<string>();

        /// <summary>Sous-score 0-100 de la catégorie Réplication. Toujours calculé.</summary>
        public int ReplicationScore { get; set; }

        /// <summary>Sous-score 0-100 de la catégorie DNS. Null si la sonde DNS n'a pas été exécutée.</summary>
        public int? DnsScore { get; set; }

        /// <summary>Sous-score 0-100 de la catégorie Réseau (tests de ports). Null si la sonde n'a pas été exécutée.</summary>
        public int? PortScore { get; set; }
    }

    public enum HealthLevel
    {
        Excellent,   // 90-100 — rien à signaler
        Warning,     // 60-89  — quelques anomalies
        Critical     // 0-59   — état dégradé sérieux
    }
}
