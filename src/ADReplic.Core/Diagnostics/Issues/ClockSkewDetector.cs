using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// DC_CLOCK_SKEW (v0.8.0) : un DC dont l'horloge s'écarte de plus de 5 minutes
    /// de la référence. Au-delà de cette tolérance Kerberos refuse les tickets :
    /// authentifications et réplication échouent de façon déroutante.
    ///
    /// Référence : le maître PDC (source de temps autoritaire de la forêt) s'il a
    /// été mesuré, sinon la médiane des décalages. Comparer les décalages entre DC
    /// annule l'horloge du poste d'audit, qui n'a donc pas besoin d'être juste.
    /// Silencieux sans sonde d'horloge ou avec moins de deux DC mesurés.
    /// </summary>
    public sealed class ClockSkewDetector : IIssueDetector
    {
        public const string Code = "DC_CLOCK_SKEW";
        public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            var measured = (snapshot?.ClockHealth?.Checks ?? Array.Empty<ClockCheckResult>())
                .Where(c => c != null && c.Status == ClockCheckStatus.Ok && c.OffsetFromAuditHost.HasValue && !string.IsNullOrWhiteSpace(c.HostName))
                .ToList();
            if (measured.Count < 2) yield break;

            var pdcHost = (snapshot.DomainControllers ?? Array.Empty<DomainControllerInfo>())
                .FirstOrDefault(dc => dc?.Roles != null && dc.Roles.Any(r => r != null && r.IndexOf("PDC", StringComparison.OrdinalIgnoreCase) >= 0))
                ?.HostName;

            var reference = measured.FirstOrDefault(c => string.Equals(c.HostName, pdcHost, StringComparison.OrdinalIgnoreCase));
            var referenceIsPdc = reference != null;
            if (reference == null) reference = Median(measured);

            foreach (var check in measured)
            {
                if (ReferenceEquals(check, reference)) continue;
                var skew = check.OffsetFromAuditHost.Value - reference.OffsetFromAuditHost.Value;
                if (skew.Duration() < Threshold) continue;

                var sign = skew < TimeSpan.Zero ? "-" : "+";
                var minutes = (int)Math.Round(skew.Duration().TotalMinutes);
                var refLabel = referenceIsPdc ? $"{reference.HostName}, maître PDC" : reference.HostName;

                yield return new DetectedIssue
                {
                    Code = Code,
                    Severity = IssueSeverity.Critical,
                    Title = $"Horloge décalée de {sign}{minutes} min : {check.HostName} (par rapport à {refLabel})",
                    Description =
                        $"L'horloge de {check.HostName} est {(skew < TimeSpan.Zero ? "en retard" : "en avance")} de " +
                        $"{minutes} min sur celle de {reference.HostName}" + (referenceIsPdc ? ", maître PDC et source de temps autoritaire de la forêt" : "") +
                        ". Au-delà de 5 min, Kerberos rejette les tickets : ce DC ne peut plus authentifier correctement ni répliquer. " +
                        (referenceIsPdc
                            ? "Vérifiez d'abord que le maître PDC est lui-même synchronisé sur une source de temps fiable."
                            : "Le maître PDC n'ayant pas pu être mesuré, la référence est la médiane des DC : vérifiez aussi sa propre synchronisation."),
                    AffectedItems = new[] { check.HostName, reference.HostName },
                    RelatedHost = check.HostName
                };
            }
        }

        private static ClockCheckResult Median(List<ClockCheckResult> measured)
        {
            var sorted = measured.OrderBy(c => c.OffsetFromAuditHost.Value).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}
