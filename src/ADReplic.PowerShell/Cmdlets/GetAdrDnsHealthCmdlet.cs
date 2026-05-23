using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Discovery;
using ADReplic.Core.Health.Dns;
using ADReplic.Core.Models;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Exécute la sonde de santé DNS d'une forêt AD : vérifie les 4 SRV
    /// critiques (_ldap, _kerberos, _gc, _kpasswd) et résout les A records
    /// de chaque DC découvert. Émet un DnsCheckResult par ligne dans le
    /// pipeline (filtrable avec Where-Object, regroupable avec Group-Object).
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRDnsHealth")]
    [OutputType(typeof(DnsCheckResult))]
    public sealed class GetAdrDnsHealthCmdlet : PSCmdlet
    {
        [Parameter(Position = 0, HelpMessage = "Nom de la forêt cible (laissez vide pour la forêt courante).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante.")]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        private readonly IDcInventoryProvider _inventoryProvider = new DcInventoryProvider();
        private readonly IDnsHealthProbe _dnsProbe = new DnsHealthProbe(new Win32DnsResolver());

        protected override void ProcessRecord()
        {
            var context = AuditContextBuilder.Build(ForestName, Credential);

            using (var cts = new CancellationTokenSource())
            {
                WriteVerbose("Découverte des contrôleurs (pour résoudre les A records)...");
                var invTask = _inventoryProvider.GetAllAsync(context, cts.Token);
                invTask.Wait(cts.Token);
                var dcs = invTask.Result;

                if (dcs.Count == 0)
                {
                    WriteWarning("Aucun contrôleur trouvé : impossible de déterminer la forêt cible.");
                    return;
                }

                var primary = dcs[0];
                WriteVerbose($"Sondage DNS sur la forêt {primary.Forest} (domaine principal {primary.Domain})...");
                var probeTask = _dnsProbe.ProbeAsync(primary.Forest, primary.Domain, dcs, cts.Token);
                probeTask.Wait(cts.Token);

                foreach (var check in probeTask.Result.Checks)
                {
                    WriteObject(check);
                }
            }
        }
    }
}
