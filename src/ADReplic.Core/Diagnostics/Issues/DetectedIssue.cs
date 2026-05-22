using System.Collections.Generic;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Anomalie détectée par un <see cref="IIssueDetector"/> sur un AuditSnapshot.
    /// Suffisamment riche pour être affichée dans la GUI, exportée dans tous les
    /// formats (CSV/JSON/HTML) et retournée par un cmdlet PowerShell.
    /// </summary>
    public sealed class DetectedIssue
    {
        /// <summary>Code stable identifiant le type d'anomalie (ex : "DC_ISOLATED").</summary>
        public string Code { get; set; }

        /// <summary>Sévérité de l'anomalie.</summary>
        public IssueSeverity Severity { get; set; }

        /// <summary>Titre court affichable en une ligne dans la GUI.</summary>
        public string Title { get; set; }

        /// <summary>Explication détaillée avec impact et recommandation de correction.</summary>
        public string Description { get; set; }

        /// <summary>Éléments concernés (DC, sites, liens) listés par leur nom.</summary>
        public IReadOnlyList<string> AffectedItems { get; set; } = new List<string>();

        public override string ToString() => $"[{Severity}] {Code} - {Title}";
    }
}
