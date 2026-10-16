using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>Une catégorie du score (Réplication, DNS, Réseau) et ce qu'elle a coûté au global.</summary>
    public sealed class ScoreCategoryBreakdown
    {
        public string Name { get; set; }

        /// <summary>Faux quand la sonde n'a pas tourné : la catégorie ne pèse pas dans le global.</summary>
        public bool IsMeasured { get; set; }

        public int? SubScore { get; set; }

        /// <summary>Poids effectif après renormalisation, en pourcent entier (0 si non mesurée).</summary>
        public int WeightPercent { get; set; }

        /// <summary>(100 − sous-score) × poids effectif : points perdus sur le score global.</summary>
        public double PointsLost { get; set; }

        public IReadOnlyList<string> Anomalies { get; set; } = new List<string>();
    }

    public sealed class ScoreBreakdown
    {
        public int Value { get; set; }
        public int TotalPointsLost => 100 - Value;
        public IReadOnlyList<ScoreCategoryBreakdown> Categories { get; set; } = new List<ScoreCategoryBreakdown>();
    }

    /// <summary>
    /// Score expliqué (v0.9.0) : rejoue la pondération de HealthScoreCalculator pour
    /// dire, par catégorie, combien de points le global a perdus et pourquoi. Les
    /// poids sont les poids EFFECTIFS (renormalisés) : la somme des points perdus
    /// correspond au badge, à l'arrondi près.
    /// </summary>
    public static class HealthScoreExplainer
    {
        public static ScoreBreakdown Explain(HealthScore score)
        {
            if (score == null) return null;

            var totalWeight = HealthScoreCalculator.WeightReplication
                + (score.DnsScore.HasValue ? HealthScoreCalculator.WeightDns : 0)
                + (score.PortScore.HasValue ? HealthScoreCalculator.WeightPorts : 0);

            return new ScoreBreakdown
            {
                Value = score.Value,
                Categories = new[]
                {
                    Category("Réplication", score.ReplicationScore, HealthScoreCalculator.WeightReplication, totalWeight, score.ReplicationAnomalies),
                    Category("DNS",         score.DnsScore,         HealthScoreCalculator.WeightDns,         totalWeight, score.DnsAnomalies),
                    Category("Réseau",      score.PortScore,        HealthScoreCalculator.WeightPorts,       totalWeight, score.PortAnomalies)
                }
            };
        }

        private static ScoreCategoryBreakdown Category(string name, int? subScore, double nominalWeight, double totalWeight, IReadOnlyList<string> anomalies)
        {
            if (!subScore.HasValue)
            {
                return new ScoreCategoryBreakdown { Name = name, IsMeasured = false, Anomalies = anomalies ?? new List<string>() };
            }

            var effectiveWeight = nominalWeight / totalWeight;
            return new ScoreCategoryBreakdown
            {
                Name = name,
                IsMeasured = true,
                SubScore = subScore,
                WeightPercent = (int)Math.Round(effectiveWeight * 100),
                PointsLost = (100 - subScore.Value) * effectiveWeight,
                Anomalies = anomalies ?? new List<string>()
            };
        }
    }
}
