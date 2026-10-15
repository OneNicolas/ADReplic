using System;
using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    public enum ClockCheckStatus
    {
        /// <summary>Heure lue, décalage mesuré.</summary>
        Ok,
        /// <summary>DC injoignable à la découverte : non interrogé.</summary>
        Skipped,
        /// <summary>Pas de réponse dans le délai.</summary>
        Timeout,
        /// <summary>Réponse absente ou illisible.</summary>
        Error
    }

    /// <summary>Lecture de l'horloge d'un DC (v0.8.0) via currentTime du RootDSE LDAP.</summary>
    public sealed class ClockCheckResult
    {
        public string HostName { get; set; }
        public ClockCheckStatus Status { get; set; }

        /// <summary>Heure UTC annoncée par le DC, null si non lue.</summary>
        public DateTime? DcTimeUtc { get; set; }

        /// <summary>Heure UTC du poste d'audit au moment de la réponse.</summary>
        public DateTime ObservedAtUtc { get; set; }

        /// <summary>
        /// DcTimeUtc − ObservedAtUtc. Positif : le DC est en avance sur le poste
        /// d'audit. Seule la DIFFÉRENCE entre deux DC est significative : l'horloge
        /// du poste d'audit s'annule dans la soustraction.
        /// </summary>
        public TimeSpan? OffsetFromAuditHost { get; set; }

        public string Detail { get; set; }
    }

    /// <summary>Résultat de la sonde d'horloge. Null dans le snapshot si la sonde n'a pas tourné.</summary>
    public sealed class ClockHealthResult
    {
        public DateTime ProbedAtUtc { get; set; }
        public IReadOnlyList<ClockCheckResult> Checks { get; set; } = new List<ClockCheckResult>();
    }
}
