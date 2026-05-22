using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Calcule un score de santé 0-100 à partir des données d'audit.
    /// Périmètre actuel : réplication uniquement (les sondes DNS/ports
    /// vivent encore côté PowerShell). Quand elles seront portées dans
    /// le Core, on étoffera le calcul ici sans changer l'API.
    ///
    /// Pondération du périmètre Réplication :
    ///   - 25 points par lien Failing/Unreachable (plafonné à 100)
    ///   - 5 points par lien Warning
    ///   - 10 points par échec actif (failure)
    ///   - 15 points par DC injoignable détecté à la découverte
    /// </summary>
    public static class HealthScoreCalculator
    {
        private const int FailingLinkPenalty   = 25;
        private const int WarningLinkPenalty   = 5;
        private const int ActiveFailurePenalty = 10;

        public static HealthScore Compute(AuditSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var anomalies = new List<string>();
            var score = 100;

            var failingLinks = snapshot.ReplicationLinks?
                .Count(l => l.Status == ReplicationLinkStatus.Failing ||
                            l.Status == ReplicationLinkStatus.Unreachable) ?? 0;
            if (failingLinks > 0)
            {
                score -= failingLinks * FailingLinkPenalty;
                anomalies.Add($"{failingLinks} lien(s) en échec ou injoignable(s)");
            }

            var warningLinks = snapshot.ReplicationLinks?
                .Count(l => l.Status == ReplicationLinkStatus.Warning) ?? 0;
            if (warningLinks > 0)
            {
                score -= warningLinks * WarningLinkPenalty;
                anomalies.Add($"{warningLinks} lien(s) en avertissement");
            }

            var activeFailures = snapshot.ReplicationFailures?.Count ?? 0;
            if (activeFailures > 0)
            {
                score -= activeFailures * ActiveFailurePenalty;
                anomalies.Add($"{activeFailures} échec(s) de réplication actifs");
            }

            if (score < 0) score = 0;

            var level = ClassifyLevel(score);
            return new HealthScore
            {
                Value     = score,
                Level     = level,
                Label     = LabelFor(level),
                Summary   = BuildSummary(failingLinks, warningLinks, activeFailures),
                Anomalies = anomalies
            };
        }

        private static HealthLevel ClassifyLevel(int score)
        {
            if (score >= 90) return HealthLevel.Excellent;
            if (score >= 60) return HealthLevel.Warning;
            return HealthLevel.Critical;
        }

        private static string LabelFor(HealthLevel level)
        {
            switch (level)
            {
                case HealthLevel.Excellent: return "Excellent";
                case HealthLevel.Warning:   return "Avertissements";
                case HealthLevel.Critical:  return "Critique";
                default: return "Inconnu";
            }
        }

        private static string BuildSummary(int failing, int warning, int activeFailures)
        {
            if (failing == 0 && warning == 0 && activeFailures == 0)
                return "Aucune anomalie détectée.";

            var parts = new List<string>();
            if (failing > 0)        parts.Add($"{failing} en échec");
            if (warning > 0)        parts.Add($"{warning} en avertissement");
            if (activeFailures > 0) parts.Add($"{activeFailures} échec(s) actif(s)");
            return string.Join(", ", parts) + ".";
        }
    }
}
