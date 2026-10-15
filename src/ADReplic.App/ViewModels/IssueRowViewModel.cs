using System.Collections.Generic;
using ADReplic.App.Mvvm;
using ADReplic.Core.Diagnostics.Issues;

namespace ADReplic.App.ViewModels
{
    /// <summary>
    /// Ligne de l'onglet Diagnostics (v0.8.0) : une anomalie de premier niveau et,
    /// pour un DC injoignable, ses conséquences repliées (ports, SRV orphelins).
    /// Enveloppe un <see cref="IssueGroup"/> du Core en y ajoutant l'état
    /// d'expansion, qui n'a de sens que pour l'affichage.
    /// </summary>
    public sealed class IssueRowViewModel : ViewModelBase
    {
        private bool _isExpanded;

        public IssueRowViewModel(IssueGroup group)
        {
            Issue    = group.Parent;
            Children = group.Children;
            DerivedSummary = group.DerivedSummary;
        }

        public DetectedIssue Issue { get; }

        public IReadOnlyList<DetectedIssue> Children { get; }

        public bool HasChildren => Children.Count > 0;

        /// <summary>« 10 anomalies liées à l'indisponibilité de srv-dc-02 », ou null.</summary>
        public string DerivedSummary { get; }

        /// <summary>Replié par défaut : l'utilisateur déplie s'il veut le détail des conséquences.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }
}
