using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Clock
{
    /// <summary>Décalage d'horloge d'un DC rapporté à la référence de la forêt.</summary>
    public sealed class ClockOffsetEntry
    {
        public string HostName { get; set; }
        public ClockCheckStatus Status { get; set; }
        public DateTime? DcTimeUtc { get; set; }
        public DateTime ObservedAtUtc { get; set; }
        public string Detail { get; set; }

        /// <summary>Vrai pour le DC qui sert de référence : son décalage est nul par construction.</summary>
        public bool IsReference { get; set; }

        /// <summary>
        /// Horloge du DC moins horloge de la référence. Positif : le DC est en avance.
        /// Null si le DC ou la référence n'a pas été mesuré, ou pour la référence elle-même.
        /// </summary>
        public TimeSpan? OffsetFromReference { get; set; }
    }

    /// <summary>
    /// Source unique du « par rapport à quoi » pour les horloges : le maître PDC
    /// (source de temps autoritaire de la forêt) s'il a été mesuré, sinon la médiane
    /// des DC mesurés. Comparer les décalages entre DC annule l'horloge du poste
    /// d'audit, qui n'a donc pas besoin d'être juste. Partagée par le détecteur
    /// DC_CLOCK_SKEW, la colonne Décalage de la GUI et la section Horloges du HTML.
    /// </summary>
    public sealed class ClockOffsetTable
    {
        public static readonly ClockOffsetTable Empty = new ClockOffsetTable(new List<ClockOffsetEntry>(), null, false, 0, false);

        private readonly Dictionary<string, ClockOffsetEntry> _byHost;

        private ClockOffsetTable(List<ClockOffsetEntry> entries, string referenceHost, bool referenceIsPdc, int measuredCount, bool hasData)
        {
            Entries = entries;
            ReferenceHost = referenceHost;
            ReferenceIsPdc = referenceIsPdc;
            MeasuredCount = measuredCount;
            HasData = hasData;
            _byHost = new Dictionary<string, ClockOffsetEntry>(HostNameNormalizer.Comparer);
            foreach (var entry in entries)
            {
                var key = HostNameNormalizer.Normalize(entry.HostName);
                if (key != null && !_byHost.ContainsKey(key)) _byHost[key] = entry;
            }
        }

        /// <summary>Faux quand la sonde d'horloge n'a pas tourné (snapshot sans ClockHealth).</summary>
        public bool HasData { get; }
        public IReadOnlyList<ClockOffsetEntry> Entries { get; }
        public string ReferenceHost { get; }
        public bool ReferenceIsPdc { get; }
        public int MeasuredCount { get; }

        public ClockOffsetEntry Find(string hostName)
        {
            var key = HostNameNormalizer.Normalize(hostName);
            return key != null && _byHost.TryGetValue(key, out var entry) ? entry : null;
        }

        public static ClockOffsetTable Build(IEnumerable<DomainControllerInfo> dcs, ClockHealthResult clockHealth)
        {
            if (clockHealth?.Checks == null) return Empty;

            var checks = clockHealth.Checks.Where(c => c != null && !string.IsNullOrWhiteSpace(c.HostName)).ToList();
            var measured = checks.Where(IsMeasured).ToList();

            var pdcHost = FindPdcHost(dcs);
            var reference = measured.FirstOrDefault(c => HostNameNormalizer.AreSame(c.HostName, pdcHost));
            var referenceIsPdc = reference != null;
            if (reference == null && measured.Count > 0) reference = Median(measured);

            var entries = checks.Select(check => new ClockOffsetEntry
            {
                HostName = check.HostName,
                Status = check.Status,
                DcTimeUtc = check.DcTimeUtc,
                ObservedAtUtc = check.ObservedAtUtc,
                Detail = check.Detail,
                IsReference = ReferenceEquals(check, reference),
                OffsetFromReference = IsMeasured(check) && reference != null && !ReferenceEquals(check, reference)
                    ? check.OffsetFromAuditHost.Value - reference.OffsetFromAuditHost.Value
                    : (TimeSpan?)null
            }).ToList();

            return new ClockOffsetTable(entries, reference?.HostName, referenceIsPdc, measured.Count, hasData: true);
        }

        private static bool IsMeasured(ClockCheckResult check)
            => check.Status == ClockCheckStatus.Ok && check.OffsetFromAuditHost.HasValue;

        private static string FindPdcHost(IEnumerable<DomainControllerInfo> dcs)
            => (dcs ?? Enumerable.Empty<DomainControllerInfo>())
                .FirstOrDefault(dc => dc?.Roles != null && dc.Roles.Any(r => r != null && r.IndexOf("PDC", StringComparison.OrdinalIgnoreCase) >= 0))
                ?.HostName;

        private static ClockCheckResult Median(List<ClockCheckResult> measured)
        {
            var sorted = measured.OrderBy(c => c.OffsetFromAuditHost.Value).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}
