using System.Collections.Generic;
using ADReplic.Core.Diagnostics.Issues;

namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Changement jugé suffisamment important pour déclencher une alerte
    /// lors d'un cycle d'auto-refresh. Filtré à partir d'un <see cref="AuditDiff"/>
    /// par <see cref="SignificantChangeDetector"/>.
    /// </summary>
    /// <remarks>
    /// La structure miroite volontairement celle de <see cref="DetectedIssue"/>
    /// pour faciliter le réemploi de converters et de templates côté GUI.
    /// </remarks>
    public sealed class SignificantChange
    {
        /// <summary>Code stable identifiant le type de changement (ex : "SC_LEVEL_DOWN").</summary>
        public string Code { get; set; }

        /// <summary>Sévérité dérivée selon la gravité du changement.</summary>
        public IssueSeverity Severity { get; set; }

        /// <summary>Titre court affichable dans un toast ou une ligne de tableau.</summary>
        public string Title { get; set; }

        /// <summary>Explication détaillée avec contexte avant/après.</summary>
        public string Description { get; set; }

        /// <summary>Éléments concernés (noms de DC, codes SRV, ports, etc.).</summary>
        public IReadOnlyList<string> AffectedItems { get; set; } = new List<string>();

        public override string ToString() => $"[{Severity}] {Code} - {Title}";
    }
}
