using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Discovery;
using ADReplic.Core.Export;
using ADReplic.Core.Health.Clock;
using ADReplic.Core.Health.Dns;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;
using ADReplic.Core.Topology;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Exécute un audit complet (inventaire + topologie + réplication + échecs)
    /// et exporte le résultat dans le format demandé.
    /// </summary>
    [Cmdlet(VerbsData.Export, "ADRAudit")]
    [OutputType(typeof(string))]
    public sealed class ExportAdrAuditCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Chemin du fichier de sortie.")]
        public string Path { get; set; }

        [Parameter(Position = 1, HelpMessage = "Format d'export.")]
        [ValidateSet("CSV", "JSON", "HTML")]
        public string Format { get; set; } = "HTML";

        [Parameter(HelpMessage = "Nom de la forêt cible (laissez vide pour la forêt courante).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "Audite ce seul DC (FQDN) sans énumérer la forêt. Le rapport reflète ce périmètre réduit.")]
        [ValidateNotNullOrEmpty]
        public string DcName { get; set; }

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante.")]
        [ValidateNotNull]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        [Parameter(HelpMessage = "Nombre maximal de DC interrogés en parallèle (défaut 8).")]
        [ValidateRange(1, 64)]
        public int Parallelism { get; set; } = 8;

        [Parameter(HelpMessage = "Désactive les sondes DNS et réseau (ports). Par défaut elles sont exécutées pour aligner le contenu du rapport sur la GUI.")]
        public SwitchParameter SkipHealthProbes { get; set; }

        protected override void ProcessRecord()
        {
            var context = AuditContextBuilder.Build(this, ForestName, Credential);
            var resolvedPath = ResolveOutputPath(Path);

            // Crée le dossier de sortie s'il n'existe pas : une tâche planifiée dont
            // le dossier a été purgé, ou un simple C:\Temp absent, ne doivent pas
            // faire échouer l'audit après plusieurs minutes de collecte.
            var outputDirectory = System.IO.Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var singleDcMode = !string.IsNullOrWhiteSpace(DcName);

            IReadOnlyList<DomainControllerInfo> dcs;
            IReadOnlyList<ReplicationLink> links;
            IReadOnlyList<ReplicationFailureInfo> failures;
            TopologySnapshot topology;
            DnsHealthResult dnsHealth = null;
            PortHealthResult portHealth = null;
            ClockHealthResult clockHealth = null;

            using (var cts = new CancellationTokenSource())
            {
                if (singleDcMode)
                {
                    WriteVerbose($"Audit du DC {DcName} (mode ciblé)...");
                    var singleTask = new DcInventoryProvider().GetSingleAsync(DcName.Trim(), context, cts.Token);
                    singleTask.Wait(cts.Token);
                    dcs = new[] { singleTask.Result };
                }
                else
                {
                    WriteVerbose("Découverte des contrôleurs...");
                    var invTask = new DcInventoryProvider().GetAllAsync(context, cts.Token);
                    invTask.Wait(cts.Token);
                    dcs = invTask.Result;
                }
                WriteVerbose($"  {dcs.Count} DC trouvés.");

                WriteVerbose("Lecture de la topologie...");
                var topoTask = new TopologyProvider().GetAsync(context, cts.Token);
                topoTask.Wait(cts.Token);
                topology = topoTask.Result;

                WriteVerbose("Sondage des liens de réplication...");
                var probeTask = new ReplicationProbe(Parallelism).ProbeAsync(dcs, context, cts.Token);
                probeTask.Wait(cts.Token);
                links = probeTask.Result;
                WriteVerbose($"  {links.Count} liens analysés.");

                WriteVerbose("Détection des échecs en cours...");
                var failTask = new ReplicationFailureProbe(Parallelism).GetFailuresAsync(dcs, context, cts.Token);
                failTask.Wait(cts.Token);
                failures = failTask.Result;
                WriteVerbose($"  {failures.Count} échec(s) actif(s).");

                if (!SkipHealthProbes.IsPresent && dcs.Count > 0)
                {
                    var primary = dcs[0];
                    WriteVerbose("Sondage DNS et réseau...");
                    var dnsTask = new DnsHealthProbe(new Win32DnsResolver())
                        .ProbeAsync(primary.Forest, primary.Domain, dcs, cts.Token);
                    var portTask = new PortHealthProbe(new TcpPortProber()).ProbeAsync(
                        dcs, AdServicePorts.Default, PortHealthProbe.DefaultPerPortTimeout, cts.Token);
                    var clockTask = new ClockHealthProbe(new LdapRootDseClockReader()).ProbeAsync(dcs, cts.Token);
                    System.Threading.Tasks.Task.WaitAll(new[] { (System.Threading.Tasks.Task)dnsTask, portTask, clockTask }, cts.Token);
                    dnsHealth = dnsTask.Result;
                    portHealth = portTask.Result;
                    clockHealth = clockTask.Result;
                    WriteVerbose($"  {dnsHealth.Checks.Count} vérifications DNS, {portHealth.Checks.Count} tests de port, {clockHealth.Checks.Count} horloges lues.");
                }
                else if (SkipHealthProbes.IsPresent)
                {
                    WriteVerbose("Sondes DNS et réseau désactivées (-SkipHealthProbes).");
                }
            }

            var forestName = dcs.Count > 0 ? dcs[0].Forest : ForestName;
            var snapshot = AuditSnapshotBuilder.Build(
                forestName, dcs, links, topology, failures,
                isSingleDcMode: singleDcMode,
                dnsHealth: dnsHealth,
                portHealth: portHealth,
                clockHealth: clockHealth);

            IAuditExporter exporter = ResolveExporter(Format);
            exporter.Export(snapshot, resolvedPath);

            WriteVerbose($"Export {Format} terminé.");
            WriteObject(resolvedPath);
        }

        private static IAuditExporter ResolveExporter(string format)
        {
            switch (format.ToUpperInvariant())
            {
                case "CSV":  return new CsvAuditExporter();
                case "JSON": return new JsonAuditExporter();
                case "HTML": return new HtmlAuditExporter();
                default: throw new ArgumentException("Format inconnu : " + format);
            }
        }

        private string ResolveOutputPath(string userPath)
        {
            if (System.IO.Path.IsPathRooted(userPath)) return userPath;
            var providerPath = SessionState.Path.CurrentFileSystemLocation.Path;
            return System.IO.Path.Combine(providerPath, userPath);
        }
    }
}
