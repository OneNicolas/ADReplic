using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Discovery;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Retourne tous les liens de réplication entrants pour les DC fournis,
    /// ou pour l'ensemble de la forêt si aucun DC n'est fourni.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRReplicationStatus")]
    [OutputType(typeof(ReplicationLink))]
    public sealed class GetAdrReplicationStatusCmdlet : PSCmdlet
    {
        [Parameter(ValueFromPipeline = true,
                   HelpMessage = "DC ciblés. Si vide, audit complet de la forêt courante.")]
        public DomainControllerInfo[] DomainControllers { get; set; }

        [Parameter(HelpMessage = "Nom de la forêt cible (laissez vide pour la forêt courante).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante.")]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        [Parameter(HelpMessage = "Nombre maximal de DC interrogés en parallèle (défaut 8).")]
        [ValidateRange(1, 64)]
        public int Parallelism { get; set; } = 8;

        private readonly List<DomainControllerInfo> _pipelineBuffer = new List<DomainControllerInfo>();

        protected override void ProcessRecord()
        {
            if (DomainControllers != null)
            {
                _pipelineBuffer.AddRange(DomainControllers);
            }
        }

        protected override void EndProcessing()
        {
            var context = AuditContextBuilder.Build(ForestName, Credential);

            IReadOnlyList<DomainControllerInfo> dcs;
            if (_pipelineBuffer.Count > 0)
            {
                dcs = _pipelineBuffer;
            }
            else
            {
                using (var cts = new CancellationTokenSource())
                {
                    var task = new DcInventoryProvider().GetAllAsync(context, cts.Token);
                    task.Wait(cts.Token);
                    dcs = task.Result;
                }
            }

            IReplicationProbe probe = new ReplicationProbe(Parallelism);
            using (var cts = new CancellationTokenSource())
            {
                var task = probe.ProbeAsync(dcs, context, cts.Token);
                task.Wait(cts.Token);

                foreach (var link in task.Result)
                {
                    WriteObject(link);
                }
            }
        }
    }
}
