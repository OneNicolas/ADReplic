using System;
using System.Collections.Generic;
using System.Windows.Input;
using ADReplic.App.Mvvm;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Comparison;
using ADReplic.Core.Export;
namespace ADReplic.App.ViewModels
{
    /// <summary>
    /// ViewModel pour la comparaison de deux audits JSON. Pilote le dialog de
    /// sélection des fichiers et l'onglet Comparaison. Volontairement indépendant
    /// de <see cref="MainViewModel"/> : on peut comparer deux audits sans
    /// que l'audit courant soit chargé.
    /// </summary>
    public sealed class CompareViewModel : ViewModelBase
    {
        private readonly IJsonAuditReader _reader;

        // Référence typée pour pouvoir déclencher RaiseCanExecuteChanged()
        // quand les chemins de fichiers changent. Le binding XAML utilise la
        // propriété publique ICommand correspondante.
        private readonly RelayCommand<object> _computeCommand;

        private string _referenceFilePath;
        private string _currentFilePath;
        private AuditDiff _diff;
        private IReadOnlyList<SignificantChange> _significantChanges;
        private string _loadError;
        private bool _isLoading;

        public CompareViewModel() : this(new JsonAuditReader()) { }

        public CompareViewModel(IJsonAuditReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));

            BrowseReferenceCommand = new RelayCommand<object>(_ => InvokeBrowse(forReference: true), _ => !IsLoading);
            BrowseCurrentCommand   = new RelayCommand<object>(_ => InvokeBrowse(forReference: false), _ => !IsLoading);
            _computeCommand        = new RelayCommand<object>(_ => ComputeDiff(), _ => !IsLoading && CanCompute);
            ComputeCommand         = _computeCommand;
            ClearCommand           = new RelayCommand<object>(_ => Clear(), _ => HasDiff || HasError);
        }

        /// <summary>
        /// Délégué injecté par la View pour ouvrir un OpenFileDialog. Retourne
        /// le chemin sélectionné ou null si l'utilisateur a annulé. Garde la
        /// View hors du ViewModel (testabilité préservée).
        /// </summary>
        public Func<string> OnPickFile { get; set; }

        // === Props observable ====================================================

        public string ReferenceFilePath
        {
            get => _referenceFilePath;
            set
            {
                if (SetProperty(ref _referenceFilePath, value))
                {
                    RaisePropertyChanged(nameof(CanCompute));
                    _computeCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string CurrentFilePath
        {
            get => _currentFilePath;
            set
            {
                if (SetProperty(ref _currentFilePath, value))
                {
                    RaisePropertyChanged(nameof(CanCompute));
                    _computeCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanCompute =>
            !string.IsNullOrWhiteSpace(_referenceFilePath)
            && !string.IsNullOrWhiteSpace(_currentFilePath);

        public AuditDiff Diff
        {
            get => _diff;
            private set
            {
                if (SetProperty(ref _diff, value))
                {
                    RaisePropertyChanged(nameof(HasDiff));
                }
            }
        }

        public IReadOnlyList<SignificantChange> SignificantChanges
        {
            get => _significantChanges;
            private set => SetProperty(ref _significantChanges, value);
        }

        public string LoadError
        {
            get => _loadError;
            private set
            {
                if (SetProperty(ref _loadError, value))
                {
                    RaisePropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_loadError);
        public bool HasDiff  => _diff != null;

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        // === Commandes ============================================================

        public ICommand BrowseReferenceCommand { get; }
        public ICommand BrowseCurrentCommand { get; }
        public ICommand ComputeCommand { get; }
        public ICommand ClearCommand { get; }

        // === Internals ============================================================

        private void InvokeBrowse(bool forReference)
        {
            var path = OnPickFile?.Invoke();
            if (string.IsNullOrEmpty(path)) return;
            if (forReference) ReferenceFilePath = path;
            else              CurrentFilePath = path;
        }

        private void ComputeDiff()
        {
            IsLoading = true;
            LoadError = null;
            try
            {
                var reference = _reader.Read(_referenceFilePath);
                var current   = _reader.Read(_currentFilePath);
                Diff = AuditDiffer.Compute(reference, current);
                SignificantChanges = SignificantChangeDetector.Detect(Diff);
            }
            catch (Exception ex)
            {
                LoadError = "Impossible de comparer les deux audits : " + ex.Message;
                Diff = null;
                SignificantChanges = null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void Clear()
        {
            ReferenceFilePath = null;
            CurrentFilePath = null;
            Diff = null;
            SignificantChanges = null;
            LoadError = null;
        }
    }
}
