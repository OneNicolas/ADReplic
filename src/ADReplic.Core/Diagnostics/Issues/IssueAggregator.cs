using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Exécute l'ensemble des <see cref="IIssueDetector"/> sur un audit
    /// et retourne la liste consolidée des anomalies, triée par sévérité
    /// décroissante puis par code, pour offrir un ordre d'affichage stable.
    ///
    /// L'API expose deux surcharges :
    /// - <see cref="Aggregate(AuditSnapshot)"/> utilise l'ensemble des
    ///   détecteurs livrés avec la lib (production).
    /// - <see cref="Aggregate(AuditSnapshot, IEnumerable{IIssueDetector})"/>
    ///   permet d'injecter une liste arbitraire (tests, scénarios custom).
    /// </summary>
    public static class IssueAggregator
    {
        public static IReadOnlyList<DetectedIssue> Aggregate(AuditSnapshot snapshot)
            => Aggregate(snapshot, BuildDefaultDetectors());

        public static IReadOnlyList<DetectedIssue> Aggregate(
            AuditSnapshot snapshot,
            IEnumerable<IIssueDetector> detectors)
        {
            if (snapshot == null) return Array.Empty<DetectedIssue>();
            if (detectors == null) return Array.Empty<DetectedIssue>();

            var all = new List<DetectedIssue>();
            foreach (var detector in detectors)
            {
                if (detector == null) continue;
                var found = detector.Detect(snapshot);
                if (found != null) all.AddRange(found);
            }

            // Tri Critical > Warning > Info, puis par code, puis par titre, pour stabilité d'affichage.
            return all
                .OrderByDescending(i => (int)i.Severity)
                .ThenBy(i => i.Code, StringComparer.Ordinal)
                .ThenBy(i => i.Title, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Énumère les détecteurs livrés avec la lib.</summary>
        public static IEnumerable<IIssueDetector> BuildDefaultDetectors()
        {
            yield return new IsolatedDcDetector();
            yield return new ObsoleteOsDetector();
            yield return new SingleDcDomainDetector();
            yield return new SiteNoBridgeheadDetector();
            yield return new SiteNoSubnetDetector();
            yield return new SiteNoDcDetector();
            yield return new SiteLinkCostDetector();
            // Détecteurs DNS (silencieux si DnsHealth == null)
            yield return new DnsSrvCriticalMissingDetector();
            yield return new DnsSrvOptionalMissingDetector();
            yield return new DnsResolutionErrorDetector();
            yield return new DnsDcARecordMissingDetector();
            // Détecteurs Ports (silencieux si PortHealth == null)
            yield return new PortCriticalClosedDetector();
            yield return new PortClosedOrFilteredDetector();
        }
    }
}
