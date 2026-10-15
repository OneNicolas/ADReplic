using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Rédige la phrase de synthèse d'un audit, en français clair, pour un
    /// lecteur non expert. Ordre des faits : DC injoignables (l'état réel est
    /// inconnu), puis échecs de réplication, puis anomalies de configuration.
    /// Aucun code, aucun GUID : les détails restent dans les onglets.
    /// </summary>
    public static class AuditSummaryWriter
    {
        public static string Write(AuditSnapshot snapshot)
        {
            if (snapshot == null) return "";

            var dcs      = snapshot.DomainControllers ?? new List<DomainControllerInfo>();
            var failures = snapshot.ReplicationFailures ?? new List<ReplicationFailureInfo>();
            var issues   = snapshot.Issues ?? new List<DetectedIssue>();

            var dcCount          = dcs.Count;
            var unreachableCount = dcs.Count(d => !d.IsReachable);
            var scope            = snapshot.IsSingleDcMode ? " (mode DC seul)" : "";

            if (unreachableCount == 0 && failures.Count == 0 && issues.Count == 0)
            {
                return $"Tout fonctionne : {Plural(dcCount, "contrôleur de domaine", "contrôleurs de domaine")}{scope}, " +
                       "aucun échec de réplication, aucune anomalie de configuration.";
            }

            var parts = new List<string>();

            if (unreachableCount > 0)
            {
                parts.Add(unreachableCount == 1
                    ? $"1 contrôleur de domaine sur {dcCount} est injoignable (son état réel est inconnu)"
                    : $"{unreachableCount} contrôleurs de domaine sur {dcCount} sont injoignables (leur état réel est inconnu)");
            }

            // Les échecs synthétiques (code -1 : "DC injoignable") sont déjà couverts
            // par la phrase sur les DC injoignables : les recompter serait redondant.
            var realFailures = failures.Where(f => f.LastErrorCode != ReplicationFailureProbe.UnreachableErrorCode).ToList();
            if (realFailures.Count > 0) parts.Add(DescribeFailures(realFailures));

            if (issues.Count > 0) parts.Add(DescribeIssues(issues));

            var sentence = string.Join(" ; ", parts) + ".";
            return snapshot.IsSingleDcMode ? sentence + " Audit en mode DC seul." : sentence;
        }

        private static string DescribeFailures(IReadOnlyList<ReplicationFailureInfo> failures)
        {
            if (failures.Count == 1)
            {
                var f = failures[0];
                return $"1 échec de réplication depuis {DurationFormatter.ToHumanText(f.FailureDuration)} " +
                       $"entre {f.SourceDc} et {f.DestinationDc} ({LowerFirst(f.ErrorTitle)})";
            }

            var oldest = failures.OrderBy(f => f.FirstFailureTime).First();
            return $"{failures.Count} échecs de réplication, le plus ancien depuis " +
                   $"{DurationFormatter.ToHumanText(oldest.FailureDuration)} ({LowerFirst(oldest.ErrorTitle)})";
        }

        private static string DescribeIssues(IReadOnlyList<DetectedIssue> issues)
        {
            var critical = issues.Count(i => i.Severity == IssueSeverity.Critical);
            var text = Plural(issues.Count, "anomalie de configuration à traiter", "anomalies de configuration à traiter");
            if (critical > 0) text += $" dont {Plural(critical, "critique", "critiques")}";
            return text;
        }

        private static string Plural(int count, string singular, string plural)
            => count + " " + (count > 1 ? plural : singular);

        /// <summary>Abaisse l'initiale pour insérer un titre en milieu de phrase, sauf pour un sigle ("DC", "RPC").</summary>
        private static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length > 1 && char.IsUpper(s[1])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}
