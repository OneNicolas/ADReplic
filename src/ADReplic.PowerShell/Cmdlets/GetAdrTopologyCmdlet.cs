using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Models;
using ADReplic.Core.Topology;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Retourne la topologie de la forêt : sites, sous-réseaux, têtes de pont, liens.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRTopology")]
    [OutputType(typeof(TopologySnapshot))]
    public sealed class GetAdrTopologyCmdlet : PSCmdlet
    {
        [Parameter(Position = 0, HelpMessage = "Nom de la forêt cible (laissez vide pour la forêt courante).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante.")]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        protected override void ProcessRecord()
        {
            var context = AuditContextBuilder.Build(ForestName, Credential);
            using (var cts = new CancellationTokenSource())
            {
                ITopologyProvider provider = new TopologyProvider();
                var task = provider.GetAsync(context, cts.Token);
                task.Wait(cts.Token);
                WriteObject(task.Result);
            }
        }
    }
}
