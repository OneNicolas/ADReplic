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
using ADReplic.Core.Health.Dns;
using ADReplic.Core.Health.Ports;
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
        private readonly IDnsHealthProbe _dnsHealthProbe;
        private readonly IPortHealthProbe _portHealthProbe;
        private readonly IReadOnlyDictionary<string, IAuditExporter> _exporters;

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

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            ExportCommand = new RelayCommand<string>(OnExport, _ => !IsBusy && HasData);
            EditCredentialsCommand = new RelayCommand<object>(_ => OnEditCredentials?.Invoke(), _ => !IsBusy);
            ResetTargetCommand = new RelayCommand<object>(_ => ResetTarget(), _ => !IsBusy);
            AuditSingleDcCommand = new RelayCommand<DomainControllerInfo>(OnAuditSingleDc, _ => !IsBusy);

            StatusMessage = "Prêt — cliquez sur Actualiser pour lancer un audit.";
        }

        public ObservableCollection<DomainControllerInfo> DomainControllers { get; }
        public ObservableCollection<ReplicationLink> ReplicationLinks { get; }
        public ObservableCollection<ReplicationFailureInfo> ReplicationFailures { get; }
        public ObservableCollection<SiteInfo> Sites { get; }
        public ObservableCollection<SiteLinkInfo> SiteLinks { get; }
        public ObservableCollection<DetectedIssue> Issues { get; }
        public ObservableCollection<DnsCheckResult> DnsChecks { get; }
        public ObservableCollection<PortCheckResult> PortChecks { get; }

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand EditCredentialsCommand { get; }
        public ICommand ResetTargetCommand { get; }
        public ICommand AuditSingleDcCommand { get; }

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

        /// <summary>
        /// DC à cibler exclusivement. Vide = audit complet de la forêt.
        /// Quand renseigné, l'inventaire ne ramene que ce DC et les détecteurs
        /// IsolatedDc / SingleDcDomain sont désactivés pour éviter les faux positifs.
        /// </summary>
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

        /// <summary>Texte court pour le header : forêt cible + DC ciblé + utilisateur si alt creds.</summary>
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

            // Snapshot du mode au début de l'audit : si l'utilisateur modifie
            // le champ pendant la collecte, on garde la cohérence avec ce qui
            // a réellement été interrogé (et donc avec ce qui sera exporté).
            var singleDcMode = IsSingleDcMode;
            var targetDc = _targetDcHostName;

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

                    // Les sondes DNS et Ports sont indépendantes : on les lance en parallèle
                    // pour gagner du temps. Aucun état partagé entre elles.
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

                    // Construction du snapshot complet pour calculer le score : on
                    // réutilise la même logique que les exports pour garantir la cohérence
                    // entre ce qui s'affiche en haut et ce qui sera dans le rapport.
                    var snapshot = AuditSnapshotBuilder.Build(
                        ForestName, dcs, links, _topology, failures, singleDcMode,
                        _lastDnsHealth, _lastPortHealth);
                    HealthScore = snapshot.HealthScore;
                    foreach (var issue in snapshot.Issues) Issues.Add(issue);

                    _lastAuditWasSingleDc = singleDcMode;
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

        /// <summary>
        /// Lance la sonde DNS avec dérivation du domaine principal depuis le premier DC.
        /// Si l'inventaire est vide ou que le DC n'a pas de domaine déclaré, retourne null
        /// (la GUI/exports gèrent gracieusement l'absence de sonde).
        /// </summary>
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
    }
}
