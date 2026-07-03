using System.Collections.Generic;

namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Différentiel structuré entre deux collections homogènes d'un même type
    /// dans deux audits. Chaque catégorie de modèle (DC, lien, issue, etc.)
    /// dispose de son propre <see cref="SnapshotDelta{T}"/> dans <see cref="AuditDiff"/>.
    /// </summary>
    /// <typeparam name="T">Type du modèle suivi.</typeparam>
    public sealed class SnapshotDelta<T>
    {
        /// <summary>Éléments présents dans l'audit courant mais absents de la référence.</summary>
        public IReadOnlyList<T> Added { get; set; } = new List<T>();

        /// <summary>Éléments présents dans la référence mais absents de l'audit courant.</summary>
        public IReadOnlyList<T> Removed { get; set; } = new List<T>();

        /// <summary>Éléments présents des deux côtés mais dont l'état pertinent a changé.</summary>
        public IReadOnlyList<DeltaChange<T>> Changed { get; set; } = new List<DeltaChange<T>>();

        /// <summary>Vrai si aucune différence n'a été détectée dans cette catégorie.</summary>
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
    }
}
