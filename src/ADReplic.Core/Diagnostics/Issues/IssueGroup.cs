using System.Collections.Generic;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Vue de présentation d'une anomalie et de ses conséquences directes.
    /// Produit par <see cref="IssueGrouper"/> ; n'est jamais sérialisé (la liste
    /// plate <c>AuditSnapshot.Issues</c> reste la source de vérité des exports,
    /// de PowerShell et de la comparaison d'audits).
    /// </summary>
    public sealed class IssueGroup
    {
        public IssueGroup(DetectedIssue parent, IReadOnlyList<DetectedIssue> children)
        {
            Parent   = parent;
            Children = children ?? new List<DetectedIssue>();
        }

        /// <summary>Anomalie de premier niveau, toujours affichée.</summary>
        public DetectedIssue Parent { get; }

        /// <summary>Anomalies dérivées du parent, repliées par défaut dans la GUI et le HTML.</summary>
        public IReadOnlyList<DetectedIssue> Children { get; }

        public int DerivedCount => Children.Count;

        public bool HasChildren => Children.Count > 0;

        /// <summary>
        /// Phrase prête à afficher sous le parent, ou null s'il n'a pas de conséquence :
        /// « 10 anomalies liées à l'indisponibilité de srv-dc-02 ».
        /// </summary>
        public string DerivedSummary
        {
            get
            {
                if (!HasChildren) return null;
                var noun = DerivedCount > 1 ? "anomalies liées" : "anomalie liée";
                return $"{DerivedCount} {noun} à l'indisponibilité de {Parent.RelatedHost}";
            }
        }
    }
}
