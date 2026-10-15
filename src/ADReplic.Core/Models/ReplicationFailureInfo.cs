using System;
using System.Web.Script.Serialization;
using ADReplic.Core.Diagnostics;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Échec de réplication en cours, retourné par GetAllReplicationFailures().
    /// Plus ciblé qu'un ReplicationLink : ne contient que les liens en panne actuelle.
    /// </summary>
    public sealed class ReplicationFailureInfo
    {
        public string DestinationDc { get; set; }
        public string SourceDc { get; set; }
        public DateTime FirstFailureTime { get; set; }
        public int ConsecutiveFailureCount { get; set; }
        public int LastErrorCode { get; set; }
        public string LastErrorMessage { get; set; }
        public ReplicationFailureSeverity Severity { get; set; }

        public TimeSpan FailureDuration => DateTime.Now - FirstFailureTime;

        /// <summary>
        /// Fiche explicative du code d'erreur (titre clair, causes, vérifications,
        /// commandes). Calculée à la demande depuis le catalogue, donc toujours
        /// disponible même pour un audit JSON archivé avant son introduction.
        /// Exclue de la sérialisation : le catalogue est la source de vérité.
        /// </summary>
        [ScriptIgnore]
        public ReplicationErrorExplanation Explanation => ReplicationErrorCatalog.Lookup(LastErrorCode);

        /// <summary>Titre lisible de l'erreur, pour les grilles et les exports.</summary>
        [ScriptIgnore]
        public string ErrorTitle => Explanation.Title;
    }

    public enum ReplicationFailureSeverity
    {
        Unknown = 0,
        Recent = 1,    // < 1h, échec transient probable
        Sustained = 2, // 1h - 24h, nécessite attention
        Critical = 3   // > 24h, risque de tombstone si > TSL/2
    }
}
