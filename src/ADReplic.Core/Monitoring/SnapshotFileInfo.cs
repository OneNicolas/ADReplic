using System;

namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Métadonnées d'un snapshot JSON persisté sur disque. Retourné par
    /// <see cref="ISnapshotStore.List"/> pour permettre à la GUI de proposer
    /// la liste des passes précédentes sans relire chaque fichier.
    /// </summary>
    public sealed class SnapshotFileInfo
    {
        /// <summary>Nom du fichier sans chemin (ex : ADReplic-2026-05-23_140523-456.json).</summary>
        public string FileName { get; set; }

        /// <summary>Chemin absolu du fichier.</summary>
        public string FullPath { get; set; }

        /// <summary>Horodatage extrait du nom de fichier (heure locale).</summary>
        public DateTime CapturedAt { get; set; }

        public override string ToString() => FileName ?? base.ToString();
    }
}
