using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Discovery;
using ADReplic.Core.Models;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Cmdlet binaire qui consomme le même IDcInventoryProvider que la GUI WPF.
    /// Garantit la cohérence entre les deux interfaces.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRDcInventory")]
    [OutputType(typeof(DomainControllerInfo))]
    public sealed class GetAdrDcInventoryCmdlet : PSCmdlet
    {
        [Parameter(Position = 0, HelpMessage = "Nom de la forêt cible (laissez vide pour la forêt courante).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante.")]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        private readonly IDcInventoryProvider _provider = new DcInventoryProvider();

        protected override void ProcessRecord()
        {
            var context = AuditContextBuilder.Build(ForestName, Credential);

            using (var cts = new CancellationTokenSource())
            {
                // Bloquant côté pipeline PowerShell : pas d'async en cmdlet, on attend.
                var task = _provider.GetAllAsync(context, cts.Token);
                task.Wait(cts.Token);

                foreach (var dc in task.Result)
                {
                    WriteObject(dc);
                }
            }
        }
    }
}
