using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Discovery;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;
using ADReplic.Core.Topology;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Exécute la collecte nécessaire (inventaire, topologie, sondage des liens)
    /// et retourne les anomalies détectées par les <see cref="IIssueDetector"/>.
    /// Filtres optionnels par sévérité et par code.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRIssue")]
    [OutputType(typeof(DetectedIssue))]
    public sealed class GetAdrIssueCmdlet : PSCmdlet
    {
        [Parameter(HelpMessage = "Nom de la forêt cible (laissez vide pour la forêt courante).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante.")]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        [Parameter(HelpMessage = "Filtre par sévérité (Info, Warning, Critical). Plusieurs valeurs possibles.")]
        public IssueSeverity[] Severity { get; set; }

        [Parameter(HelpMessage = "Filtre par code d'anomalie (ex: DC_ISOLATED). Plusieurs valeurs possibles.")]
        public string[] Code { get; set; }

        [Parameter(HelpMessage = "Nombre maximal de DC interrogés en parallèle (défaut 8).")]
        [ValidateRange(1, 64)]
        public int Parallelism { get; set; } = 8;

        protected override void ProcessRecord()
        {
            var context = AuditContextBuilder.Build(ForestName, Credential);

            IReadOnlyList<DomainControllerInfo> dcs;
            IReadOnlyList<ReplicationLink> links;
            TopologySnapshot topology;

            using (var cts = new CancellationTokenSource())
            {
                WriteVerbose("Découverte des contrôleurs...");
                var invTask = new DcInventoryProvider().GetAllAsync(context, cts.Token);
                invTask.Wait(cts.Token);
                dcs = invTask.Result;

                WriteVerbose("Lecture de la topologie...");
                var topoTask = new TopologyProvider().GetAsync(context, cts.Token);
                topoTask.Wait(cts.Token);
                topology = topoTask.Result;

                WriteVerbose("Sondage des liens de réplication...");
                var probeTask = new ReplicationProbe(Parallelism).ProbeAsync(dcs, context, cts.Token);
                probeTask.Wait(cts.Token);
                links = probeTask.Result;
            }

            var forestName = dcs.Count > 0 ? dcs[0].Forest : ForestName;
            var snapshot = AuditSnapshotBuilder.Build(forestName, dcs, links, topology);

            foreach (var issue in FilterIssues(snapshot.Issues))
            {
                WriteObject(issue);
            }
        }

        private IEnumerable<DetectedIssue> FilterIssues(IReadOnlyList<DetectedIssue> issues)
        {
            if (issues == null) yield break;

            var severityFilter = (Severity != null && Severity.Length > 0)
                ? new HashSet<IssueSeverity>(Severity)
                : null;

            var codeFilter = (Code != null && Code.Length > 0)
                ? new HashSet<string>(Code, System.StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var issue in issues)
            {
                if (severityFilter != null && !severityFilter.Contains(issue.Severity)) continue;
                if (codeFilter != null && !codeFilter.Contains(issue.Code)) continue;
                yield return issue;
            }
        }
    }
}
