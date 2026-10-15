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

        /// <summary>
        /// Constat : ce qui a été observé, en une ou deux phrases. Conservé tel quel
        /// pour la compatibilité des exports et des audits JSON archivés ; les champs
        /// structurés ci-dessous le complètent sans le remplacer.
        /// </summary>
        public string Description { get; set; }

        /// <summary>Pourquoi c'est important : conséquence concrète pour les utilisateurs ou la forêt.</summary>
        public string Impact { get; set; }

        /// <summary>Que faire : vérifications et corrections, du plus simple au plus intrusif.</summary>
        public string Recommendation { get; set; }

        /// <summary>Article de référence (Microsoft Learn de préférence), ou null.</summary>
        public string DocumentationUrl { get; set; }

        /// <summary>Éléments concernés (DC, sites, liens) listés par leur nom.</summary>
        public IReadOnlyList<string> AffectedItems { get; set; } = new List<string>();

        /// <summary>
        /// Nom du DC auquel l'anomalie se rattache, ou null si elle ne concerne pas
        /// un DC précis (topologie, domaine). Sert de pivot à l'agrégation de
        /// présentation (<see cref="IssueGrouper"/>) : les conséquences d'un DC
        /// injoignable sont repliées derrière son DC_UNREACHABLE. Ne participe
        /// pas à l'identité de l'anomalie dans la comparaison d'audits.
        /// </summary>
        public string RelatedHost { get; set; }

        public override string ToString() => $"[{Severity}] {Code} - {Title}";
    }
}
