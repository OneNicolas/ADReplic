using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ADReplic.App.Mvvm;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Discovery;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;
using ADReplic.Core.Topology;

namespace ADReplic.App.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly IDcInventoryProvider _inventoryProvider;
        private readonly IReplicationProbe _replicationProbe;
        private readonly IReplicationFailureProbe _failureProbe;
        private readonly ITopologyProvider _topologyProvider;
        private readonly IReadOnlyDictionary<string, IAuditExporter> _exporters;

        private string _statusMessage;
        private string _forestName;
        private string _targetForestName;
        private string _credentialUserName;
        private string _credentialPassword;
        private bool _isBusy;
        private TopologySnapshot _topology;
        private HealthScore _healthScore;

        public MainViewModel()
            : this(new DcInventoryProvider(), new ReplicationProbe(), new ReplicationFailureProbe(),
                   new TopologyProvider(), DefaultExporters())
        {
        }

        public MainViewModel(
            IDcInventoryProvider inventoryProvider,
            IReplicationProbe replicationProbe,
            IReplicationFailureProbe failureProbe,
            ITopologyProvider topologyProvider,
            IEnumerable<IAuditExporter> exporters)
        {
            _inventoryProvider = inventoryProvider ?? throw new ArgumentNullException(nameof(inventoryProvider));
            _replicationProbe = replicationProbe ?? throw new ArgumentNullException(nameof(replicationProbe));
            _failureProbe = failureProbe ?? throw new ArgumentNullException(nameof(failureProbe));
            _topologyProvider = topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
            _exporters = (exporters ?? Enumerable.Empty<IAuditExporter>())
                .ToDictionary(e => e.Format, StringComparer.OrdinalIgnoreCase);

            DomainControllers = new ObservableCollection<DomainControllerInfo>();
            ReplicationLinks = new ObservableCollection<ReplicationLink>();
            ReplicationFailures = new ObservableCollection<ReplicationFailureInfo>();
            Sites = new ObservableCollection<SiteInfo>();
            SiteLinks = new ObservableCollection<SiteLinkInfo>();
            Issues = new ObservableCollection<DetectedIssue>();

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            ExportCommand = new RelayCommand<string>(OnExport, _ => !IsBusy && HasData);
            EditCredentialsCommand = new RelayCommand<object>(_ => OnEditCredentials?.Invoke(), _ => !IsBusy);
            ResetTargetCommand = new RelayCommand<object>(_ => ResetTarget(), _ => !IsBusy);

            StatusMessage = "Prêt — cliquez sur Actualiser pour lancer un audit.";
        }

        public ObservableCollection<DomainControllerInfo> DomainControllers { get; }
        public ObservableCollection<ReplicationLink> ReplicationLinks { get; }
        public ObservableCollection<ReplicationFailureInfo> ReplicationFailures { get; }
        public ObservableCollection<SiteInfo> Sites { get; }
        public ObservableCollection<SiteLinkInfo> SiteLinks { get; }
        public ObservableCollection<DetectedIssue> Issues { get; }

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand EditCredentialsCommand { get; }
        public ICommand ResetTargetCommand { get; }

        public Func<string, string, string> SaveFilePicker { get; set; }

        /// <summary>Délégué appelé par la View pour ouvrir le dialog credentials.</summary>
        public Action OnEditCredentials { get; set; }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>Forêt effectivement découverte lors du dernier audit.</summary>
        public string ForestName
        {
            get => _forestName;
            set => SetProperty(ref _forestName, value);
        }

        /// <summary>Cible saisie par l'utilisateur (vide = forêt courante).</summary>
        public string TargetForestName
        {
            get => _targetForestName;
            set
            {
                if (SetProperty(ref _targetForestName, value))
                {
                    RaisePropertyChanged(nameof(TargetSummary));
                }
            }
        }

        public string CredentialUserName
        {
            get => _credentialUserName;
            private set
            {
                if (SetProperty(ref _credentialUserName, value))
                {
                    RaisePropertyChanged(nameof(HasAlternateCredentials));
                    RaisePropertyChanged(nameof(TargetSummary));
                }
            }
        }

        public bool HasAlternateCredentials =>
            !string.IsNullOrEmpty(_credentialUserName) && !string.IsNullOrEmpty(_credentialPassword);

        /// <summary>Texte court pour le header : forêt cible + utilisateur si alt creds.</summary>
        public string TargetSummary
        {
            get
            {
                var forest = string.IsNullOrWhiteSpace(_targetForestName) ? "(forêt courante)" : _targetForestName;
                if (HasAlternateCredentials) return $"Cible : {forest} · en tant que {_credentialUserName}";
                return $"Cible : {forest}";
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public bool HasData => DomainControllers.Count > 0 || ReplicationLinks.Count > 0;

        /// <summary>Score de santé du dernier audit (null tant qu'aucun audit n'a tourné).</summary>
        public HealthScore HealthScore
        {
            get => _healthScore;
            private set
            {
                if (SetProperty(ref _healthScore, value))
                {
                    RaisePropertyChanged(nameof(HasHealthScore));
                }
            }
        }

        public bool HasHealthScore => _healthScore != null;

        /// <summary>Appelé par la View après fermeture du dialog credentials.</summary>
        public void ApplyCredentials(string userName, string password)
        {
            _credentialPassword = password;
            CredentialUserName = userName;
        }

        private void ResetTarget()
        {
            TargetForestName = null;
            ApplyCredentials(null, null);
            StatusMessage = "Cible réinitialisée : forêt courante, identifiants Windows.";
        }

        private static IEnumerable<IAuditExporter> DefaultExporters() => new IAuditExporter[]
        {
            new CsvAuditExporter(),
            new JsonAuditExporter(),
            new HtmlAuditExporter()
        };

        private AuditContext BuildContext()
        {
            return new AuditContext
            {
                ForestName = string.IsNullOrWhiteSpace(_targetForestName) ? null : _targetForestName.Trim(),
                Username   = HasAlternateCredentials ? _credentialUserName : null,
                Password   = HasAlternateCredentials ? _credentialPassword : null,
            };
        }

        private async Task RefreshAllAsync()
        {
            IsBusy = true;
            DomainControllers.Clear();
            ReplicationLinks.Clear();
            ReplicationFailures.Clear();
            Sites.Clear();
            SiteLinks.Clear();
            Issues.Clear();
            _topology = null;
            ForestName = null;
            HealthScore = null;

            try
            {
                var context = BuildContext();
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                {
                    StatusMessage = "Découverte des contrôleurs...";
                    var dcs = await _inventoryProvider.GetAllAsync(context, cts.Token);
                    foreach (var dc in dcs)
                    {
                        DomainControllers.Add(dc);
                        if (string.IsNullOrEmpty(ForestName)) ForestName = dc.Forest;
                    }

                    StatusMessage = "Lecture de la topologie...";
                    _topology = await _topologyProvider.GetAsync(context, cts.Token);
                    foreach (var site in _topology.Sites) Sites.Add(site);
                    foreach (var link in _topology.SiteLinks) SiteLinks.Add(link);

                    StatusMessage = $"Sondage de la réplication ({dcs.Count} DC)...";
                    var links = await _replicationProbe.ProbeAsync(dcs, context, cts.Token);
                    foreach (var l in links) ReplicationLinks.Add(l);

                    StatusMessage = "Détection des échecs en cours...";
                    var failures = await _failureProbe.GetFailuresAsync(dcs, context, cts.Token);
                    foreach (var f in failures) ReplicationFailures.Add(f);

                    StatusMessage = BuildFinalStatus(dcs.Count, links, failures);

                    // Construction du snapshot complet pour calculer le score : on
                    // réutilise la même logique que les exports pour garantir la cohérence
                    // entre ce qui s'affiche en haut et ce qui sera dans le rapport.
                    var snapshot = AuditSnapshotBuilder.Build(
                        ForestName, dcs, links, _topology, failures);
                    HealthScore = snapshot.HealthScore;
                    foreach (var issue in snapshot.Issues) Issues.Add(issue);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Audit interrompu (timeout).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur : " + AuditErrorClassifier.Explain(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnExport(string format)
        {
            if (string.IsNullOrEmpty(format)) return;
            if (!_exporters.TryGetValue(format, out var exporter))
            {
                StatusMessage = $"Format inconnu : {format}";
                return;
            }
            if (SaveFilePicker == null)
            {
                StatusMessage = "Aucun sélecteur de fichier configuré.";
                return;
            }

            var defaultName = $"ADReplic_{DateTime.Now:yyyyMMdd_HHmmss}{exporter.DefaultFileExtension}";
            var target = SaveFilePicker(exporter.Format, defaultName);
            if (string.IsNullOrEmpty(target)) return;

            try
            {
                var snapshot = AuditSnapshotBuilder.Build(
                    ForestName,
                    DomainControllers.ToList(),
                    ReplicationLinks.ToList(),
                    _topology,
                    ReplicationFailures.ToList());
                exporter.Export(snapshot, target);
                StatusMessage = $"Export {exporter.Format} : {target}";
            }
            catch (Exception ex)
            {
                StatusMessage = "Erreur export : " + ex.Message;
            }
        }

        private static string BuildFinalStatus(
            int dcCount,
            IReadOnlyList<ReplicationLink> links,
            IReadOnlyList<ReplicationFailureInfo> failures)
        {
            int failing = 0, warning = 0;
            foreach (var l in links)
            {
                if (l.Status == ReplicationLinkStatus.Failing || l.Status == ReplicationLinkStatus.Unreachable) failing++;
                else if (l.Status == ReplicationLinkStatus.Warning) warning++;
            }
            var failureLabel = failures.Count > 0 ? $", {failures.Count} échec(s) actifs" : "";
            return $"{dcCount} DC, {links.Count} liens — {failing} en échec, {warning} en avertissement{failureLabel}.";
        }
    }
}
