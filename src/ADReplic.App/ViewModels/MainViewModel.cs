using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using ADReplic.App.Mvvm;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Comparison;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Discovery;
using ADReplic.Core.Export;
using ADReplic.Core.Health.Dns;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;
using ADReplic.Core.Monitoring;
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
        private readonly IDnsHealthProbe _dnsHealthProbe;
        private readonly IPortHealthProbe _portHealthProbe;
        private readonly IReadOnlyDictionary<string, IAuditExporter> _exporters;

        // === Monitoring (v0.5.0) ============================================
        private readonly ISnapshotStore _snapshotStore;
        private readonly AuditHistory _scoreHistory;
        private readonly MonitoringSettingsStore _settingsStore;
        private readonly DispatcherTimer _autoRefreshTimer;
        private AuditSnapshot _lastSnapshotForAlert;

        private string _statusMessage;
        private string _forestName;
        private string _targetForestName;
        private string _targetDcHostName;
        private string _credentialUserName;
        private string _credentialPassword;
        private bool _isBusy;
        private bool _lastAuditWasSingleDc;
        private TopologySnapshot _topology;
        private DnsHealthResult _lastDnsHealth;
        private PortHealthResult _lastPortHealth;
        private HealthScore _healthScore;

        // === Monitoring state ===============================================
        private bool _isAutoRefreshEnabled;
        private int _autoRefreshIntervalMinutes;
        private bool _hasPendingAlert;
        private string _pendingAlertSummary;
        private IReadOnlyList<SignificantChange> _lastSignificantChanges;

        public MainViewModel()
            : this(new DcInventoryProvider(), new ReplicationProbe(), new ReplicationFailureProbe(),
                   new TopologyProvider(),
                   new DnsHealthProbe(new Win32DnsResolver()),
                   new PortHealthProbe(new TcpPortProber()),
                   DefaultExporters())
        {
        }

        public MainViewModel(
            IDcInventoryProvider inventoryProvider,
            IReplicationProbe replicationProbe,
            IReplicationFailureProbe failureProbe,
            ITopologyProvider topologyProvider,
            IDnsHealthProbe dnsHealthProbe,
            IPortHealthProbe portHealthProbe,
            IEnumerable<IAuditExporter> exporters)
        {
            _inventoryProvider = inventoryProvider ?? throw new ArgumentNullException(nameof(inventoryProvider));
            _replicationProbe = replicationProbe ?? throw new ArgumentNullException(nameof(replicationProbe));
            _failureProbe = failureProbe ?? throw new ArgumentNullException(nameof(failureProbe));
            _topologyProvider = topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
            _dnsHealthProbe = dnsHealthProbe ?? throw new ArgumentNullException(nameof(dnsHealthProbe));
            _portHealthProbe = portHealthProbe ?? throw new ArgumentNullException(nameof(portHealthProbe));
            _exporters = (exporters ?? Enumerable.Empty<IAuditExporter>())
                .ToDictionary(e => e.Format, StringComparer.OrdinalIgnoreCase);

            DomainControllers = new ObservableCollection<DomainControllerInfo>();
            ReplicationLinks = new ObservableCollection<ReplicationLink>();
            ReplicationFailures = new ObservableCollection<ReplicationFailureInfo>();
            Sites = new ObservableCollection<SiteInfo>();
            SiteLinks = new ObservableCollection<SiteLinkInfo>();
            Issues = new ObservableCollection<DetectedIssue>();
            DnsChecks = new ObservableCollection<DnsCheckResult>();
            PortChecks = new ObservableCollection<PortCheckResult>();
            ScoreHistoryEntries = new ObservableCollection<ScoreHistoryEntry>();

            // === Monitoring : init des composants Core ======================
            _snapshotStore = new SnapshotStore();
            _scoreHistory = new AuditHistory();
            _settingsStore = new MonitoringSettingsStore();
            var settings = _settingsStore.Load();
            _autoRefreshIntervalMinutes = settings.IntervalMinutes;
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_autoRefreshIntervalMinutes)
            };
            _autoRefreshTimer.Tick += OnAutoRefreshTick;
            // Le toggle reste OFF par défaut au démarrage, peu importe l'état précédent
            // (décision actée : éviter qu'un audit non sollicité parte au lancement).

            Compare = new CompareViewModel();

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            ExportCommand = new RelayCommand<string>(OnExport, _ => !IsBusy && HasData);
            EditCredentialsCommand = new RelayCommand<object>(_ => OnEditCredentials?.Invoke(), _ => !IsBusy);
            ResetTargetCommand = new RelayCommand<object>(_ => ResetTarget(), _ => !IsBusy);
            AuditSingleDcCommand = new RelayCommand<DomainControllerInfo>(OnAuditSingleDc, _ => !IsBusy);

            OpenCompareDialogCommand    = new RelayCommand<object>(_ => OnOpenCompareDialog?.Invoke());
            ToggleAutoRefreshCommand    = new RelayCommand<object>(_ => IsAutoRefreshEnabled = !IsAutoRefreshEnabled);
            OpenSnapshotsFolderCommand  = new RelayCommand<object>(_ => OpenSnapshotsFolder());
            AcknowledgeAlertCommand     = new RelayCommand<object>(_ => AcknowledgeAlert(), _ => HasPendingAlert);
            SetAutoRefreshIntervalCommand = new RelayCommand<object>(SetAutoRefreshInterval);

            StatusMessage = "Prêt — cliquez sur Actualiser pour lancer un audit.";
        }

        // === Observable collections (existantes) ============================
        public ObservableCollection<DomainControllerInfo> DomainControllers { get; }
        public ObservableCollection<ReplicationLink> ReplicationLinks { get; }
        public ObservableCollection<ReplicationFailureInfo> ReplicationFailures { get; }
        public ObservableCollection<SiteInfo> Sites { get; }
        public ObservableCollection<SiteLinkInfo> SiteLinks { get; }
        public ObservableCollection<DetectedIssue> Issues { get; }
        public ObservableCollection<DnsCheckResult> DnsChecks { get; }
        public ObservableCollection<PortCheckResult> PortChecks { get; }

        // === Observable collections (nouvelles v0.5.0) ======================
        public ObservableCollection<ScoreHistoryEntry> ScoreHistoryEntries { get; }

        // === Commandes (existantes) =========================================
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand EditCredentialsCommand { get; }
        public ICommand ResetTargetCommand { get; }
        public ICommand AuditSingleDcCommand { get; }

        // === Commandes (nouvelles v0.5.0) ===================================
        public ICommand OpenCompareDialogCommand { get; }
        public ICommand ToggleAutoRefreshCommand { get; }
        public ICommand OpenSnapshotsFolderCommand { get; }
        public ICommand AcknowledgeAlertCommand { get; }
        public ICommand SetAutoRefreshIntervalCommand { get; }

        // === Sous-VMs =======================================================
        public CompareViewModel Compare { get; }

        // === Délégués View -> VM (existant + nouveau) =======================
        public Func<string, string, string> SaveFilePicker { get; set; }
        public Action OnEditCredentials { get; set; }
        public Action OnOpenCompareDialog { get; set; }
        public Action OnShowAlert { get; set; }

        /// <summary>Intervalles proposés dans le menu (en minutes).</summary>
        public static IReadOnlyList<int> AutoRefreshIntervalOptions { get; } = new[] { 5, 15, 30, 60 };

        // === Props (existantes) =============================================

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ForestName
        {
            get => _forestName;
            set => SetProperty(ref _forestName, value);
        }

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

        public string TargetDcHostName
        {
            get => _targetDcHostName;
            set
            {
                if (SetProperty(ref _targetDcHostName, value))
                {
                    RaisePropertyChanged(nameof(IsSingleDcMode));
                    RaisePropertyChanged(nameof(TargetSummary));
                }
            }
        }

        public bool IsSingleDcMode => !string.IsNullOrWhiteSpace(_targetDcHostName);

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

        public string TargetSummary
        {
            get
            {
                var forest = string.IsNullOrWhiteSpace(_targetForestName) ? "(forêt courante)" : _targetForestName;
                var parts = new List<string> { "Cible : " + forest };
                if (IsSingleDcMode)         parts.Add("DC seul : " + _targetDcHostName);
                if (HasAlternateCredentials) parts.Add("en tant que " + _credentialUserName);
                return string.Join(" · ", parts);
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public bool HasData => DomainControllers.Count > 0 || ReplicationLinks.Count > 0;

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

        // === Props (nouvelles v0.5.0) =======================================

        public bool IsAutoRefreshEnabled
        {
            get => _isAutoRefreshEnabled;
            set
            {
                if (SetProperty(ref _isAutoRefreshEnabled, value))
                {
                    if (value) StartAutoRefresh();
                    else       StopAutoRefresh();
                }
            }
        }

        public int AutoRefreshIntervalMinutes
        {
            get => _autoRefreshIntervalMinutes;
            set
            {
                if (value < 1) return;
                if (SetProperty(ref _autoRefreshIntervalMinutes, value))
                {
                    ApplyIntervalChange();
                    PersistSettings();
                }
            }
        }

        public bool HasPendingAlert
        {
            get => _hasPendingAlert;
            private set => SetProperty(ref _hasPendingAlert, value);
        }

        public string PendingAlertSummary
        {
            get => _pendingAlertSummary;
            private set => SetProperty(ref _pendingAlertSummary, value);
        }

        public IReadOnlyList<SignificantChange> LastSignificantChanges
        {
            get => _lastSignificantChanges;
            private set
            {
                if (SetProperty(ref _lastSignificantChanges, value))
                {
                    RaisePropertyChanged(nameof(HasSignificantChanges));
                }
            }
        }

        public bool HasSignificantChanges =>
            _lastSignificantChanges != null && _lastSignificantChanges.Count > 0;

        public string SnapshotsFolderPath => _snapshotStore?.BaseDirectory;

        // === Comportement (existant) ========================================

        public void ApplyCredentials(string userName, string password)
        {
            _credentialPassword = password;
            CredentialUserName = userName;
        }

        private void ResetTarget()
        {
            TargetForestName = null;
            TargetDcHostName = null;
            ApplyCredentials(null, null);
            StatusMessage = "Cible réinitialisée : forêt courante, identifiants Windows.";
        }

        private void OnAuditSingleDc(DomainControllerInfo dc)
        {
            if (dc == null || string.IsNullOrEmpty(dc.HostName)) return;
            TargetDcHostName = dc.HostName;
            if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
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
            DnsChecks.Clear();
            PortChecks.Clear();
            _topology = null;
            _lastDnsHealth = null;
            _lastPortHealth = null;
            _lastAuditWasSingleDc = false;
            ForestName = null;
            HealthScore = null;

            var singleDcMode = IsSingleDcMode;
            var targetDc = _targetDcHostName;

            AuditSnapshot finalSnapshot = null;

            try
            {
                var context = BuildContext();
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                {
                    IReadOnlyList<DomainControllerInfo> dcs;
                    if (singleDcMode)
                    {
                        StatusMessage = $"Audit du DC {targetDc}...";
                        var single = await _inventoryProvider.GetSingleAsync(targetDc, context, cts.Token);
                        dcs = new[] { single };
                    }
                    else
                    {
                        StatusMessage = "Découverte des contrôleurs...";
                        dcs = await _inventoryProvider.GetAllAsync(context, cts.Token);
                    }

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

                    StatusMessage = "Sondage DNS et réseau...";
                    var dnsTask = ProbeDnsAsync(dcs, cts.Token);
                    var portTask = _portHealthProbe.ProbeAsync(
                        dcs, AdServicePorts.Default, PortHealthProbe.DefaultPerPortTimeout, cts.Token);
                    await Task.WhenAll(dnsTask, portTask).ConfigureAwait(true);
                    _lastDnsHealth = await dnsTask;
                    _lastPortHealth = await portTask;
                    if (_lastDnsHealth?.Checks != null)
                        foreach (var c in _lastDnsHealth.Checks) DnsChecks.Add(c);
                    if (_lastPortHealth?.Checks != null)
                        foreach (var c in _lastPortHealth.Checks) PortChecks.Add(c);

                    StatusMessage = BuildFinalStatus(dcs.Count, links, failures);

                    var snapshot = AuditSnapshotBuilder.Build(
                        ForestName, dcs, links, _topology, failures, singleDcMode,
                        _lastDnsHealth, _lastPortHealth);
                    HealthScore = snapshot.HealthScore;
                    foreach (var issue in snapshot.Issues) Issues.Add(issue);

                    _lastAuditWasSingleDc = singleDcMode;
                    finalSnapshot = snapshot;
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

            // Hook v0.5.0 : historique + (si auto-refresh) persistence et diff vs N-1.
            // Le hook est volontairement hors du try/catch : son échec ne doit pas
            // contaminer un audit par ailleurs réussi.
            if (finalSnapshot != null)
            {
                HandlePostRefresh(finalSnapshot);
            }
        }

        private async Task<DnsHealthResult> ProbeDnsAsync(
            IReadOnlyList<DomainControllerInfo> dcs, CancellationToken cancellationToken)
        {
            var primaryDc = dcs?.FirstOrDefault(d => !string.IsNullOrEmpty(d?.Forest) && !string.IsNullOrEmpty(d?.Domain));
            if (primaryDc == null) return null;

            return await _dnsHealthProbe.ProbeAsync(
                primaryDc.Forest, primaryDc.Domain, dcs, cancellationToken);
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
                    ReplicationFailures.ToList(),
                    _lastAuditWasSingleDc,
                    _lastDnsHealth,
                    _lastPortHealth);
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

        // === Monitoring (v0.5.0) ============================================

        private void HandlePostRefresh(AuditSnapshot snapshot)
        {
            if (snapshot?.HealthScore != null)
            {
                AddToHistory(ScoreHistoryEntry.From(snapshot.HealthScore));
            }

            // Hors mode auto-refresh : on enregistre juste l'historique en mémoire.
            // Pas d'écriture disque, pas de diff, pas d'alerte. L'utilisateur en
            // mode manuel utilise le bouton "Comparer..." pour confronter 2 audits.
            if (!_isAutoRefreshEnabled) return;

            try
            {
                _snapshotStore.Save(snapshot);
            }
            catch (Exception ex)
            {
                StatusMessage = "Snapshot non sauvegardé : " + ex.Message;
            }

            if (_lastSnapshotForAlert != null)
            {
                try
                {
                    var diff = AuditDiffer.Compute(_lastSnapshotForAlert, snapshot);
                    var changes = SignificantChangeDetector.Detect(diff);
                    if (changes.Count > 0)
                    {
                        LastSignificantChanges = changes;
                        PendingAlertSummary = BuildAlertSummary(changes);
                        HasPendingAlert = true;
                        OnShowAlert?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "Diff post-refresh impossible : " + ex.Message;
                }
            }

            _lastSnapshotForAlert = snapshot;
        }

        private void AddToHistory(ScoreHistoryEntry entry)
        {
            _scoreHistory.Add(entry);
            ScoreHistoryEntries.Add(entry);

            // Garder la collection observable alignée sur la capacité du ring buffer.
            while (ScoreHistoryEntries.Count > _scoreHistory.Capacity)
            {
                ScoreHistoryEntries.RemoveAt(0);
            }
        }

        private static string BuildAlertSummary(IReadOnlyList<SignificantChange> changes)
        {
            if (changes == null || changes.Count == 0) return string.Empty;
            if (changes.Count == 1) return changes[0].Title;
            return $"{changes.Count} changements significatifs détectés (premier : {changes[0].Title})";
        }

        private void StartAutoRefresh()
        {
            _autoRefreshTimer.Interval = TimeSpan.FromMinutes(_autoRefreshIntervalMinutes);
            _autoRefreshTimer.Start();
            StatusMessage = $"Surveillance activée — passe toutes les {_autoRefreshIntervalMinutes} min.";
        }

        private void StopAutoRefresh()
        {
            _autoRefreshTimer.Stop();
            _lastSnapshotForAlert = null;
            StatusMessage = "Surveillance désactivée.";
        }

        private void OnAutoRefreshTick(object sender, EventArgs e)
        {
            // Anti-marteau : si l'audit précédent n'est pas encore terminé,
            // on saute cette passe plutôt que d'enfiler les requêtes AD.
            if (IsBusy) return;
            if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
        }

        private void ApplyIntervalChange()
        {
            _autoRefreshTimer.Interval = TimeSpan.FromMinutes(_autoRefreshIntervalMinutes);
            // Si actif, on redémarre proprement pour appliquer immédiatement le nouvel intervalle.
            if (_autoRefreshTimer.IsEnabled)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Start();
            }
        }

        private void SetAutoRefreshInterval(object parameter)
        {
            // Le CommandParameter XAML est typé string par défaut. On accepte aussi int
            // pour les appels code-behind / tests, et on parse en culture invariante.
            if (parameter is int direct)
            {
                AutoRefreshIntervalMinutes = direct;
                return;
            }
            if (parameter == null) return;
            if (int.TryParse(
                    parameter.ToString(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
            {
                AutoRefreshIntervalMinutes = parsed;
            }
        }

        private void PersistSettings()
        {
            try
            {
                _settingsStore.Save(new MonitoringSettings
                {
                    IntervalMinutes = _autoRefreshIntervalMinutes
                });
            }
            catch (Exception ex)
            {
                // Pas critique : on continue, on aura simplement les valeurs par defaut
                // au prochain demarrage.
                StatusMessage = "Préférences non sauvegardées : " + ex.Message;
            }
        }

        private void AcknowledgeAlert()
        {
            HasPendingAlert = false;
            // On laisse LastSignificantChanges intact : l'utilisateur peut toujours
            // consulter ce qui a déclenché l'alerte tant qu'un nouveau cycle ne l'a
            // pas écrasé.
        }

        private void OpenSnapshotsFolder()
        {
            var folder = _snapshotStore?.BaseDirectory;
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "Impossible d'ouvrir le dossier : " + ex.Message;
            }
        }
    }
}
