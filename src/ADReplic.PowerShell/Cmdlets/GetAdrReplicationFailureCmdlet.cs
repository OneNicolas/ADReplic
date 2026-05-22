using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Discovery;
using ADReplic.Core.Models;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Retourne uniquement les échecs de réplication en cours.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRReplicationFailure")]
    [OutputType(typeof(ReplicationFailureInfo))]
    public sealed class GetAdrReplicationFailureCmdlet : PSCmdlet
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
            if (DomainControllers != null) _pipelineBuffer.AddRange(DomainControllers);
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

            IReplicationFailureProbe probe = new ReplicationFailureProbe(Parallelism);
            using (var cts = new CancellationTokenSource())
            {
                var task = probe.GetFailuresAsync(dcs, context, cts.Token);
                task.Wait(cts.Token);
                foreach (var failure in task.Result)
                {
                    WriteObject(failure);
                }
            }
        }
    }
}
