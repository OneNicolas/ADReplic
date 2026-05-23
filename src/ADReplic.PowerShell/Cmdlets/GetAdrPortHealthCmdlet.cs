using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Discovery;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;
using ADReplic.PowerShell.Internal;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Teste la connectivité TCP des ports AD critiques sur un ou plusieurs DC.
    /// Sans -DCHostName, découvre les DC de la forêt et teste chacun ; avec
    /// -DCHostName, ne teste que les hôtes donnés (utile pour cibler un audit).
    /// Sans -Port, teste les 9 ports par défaut (53, 88, 135, 389, 445, 464,
    /// 636, 3268, 3269) ; avec -Port, ne teste que ceux fournis.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRPortHealth")]
    [OutputType(typeof(PortCheckResult))]
    public sealed class GetAdrPortHealthCmdlet : PSCmdlet
    {
        [Parameter(Position = 0, HelpMessage = "Nom de la forêt cible (utilisé si DCHostName est vide).")]
        public string ForestName { get; set; }

        [Parameter(HelpMessage = "FQDN des contrôleurs à tester. Si vide, découverte automatique via la forêt.")]
        public string[] DCHostName { get; set; }

        [Parameter(HelpMessage = "Ports à tester. Si vide, utilise la liste AD par défaut (9 ports).")]
        [ValidateRange(1, 65535)]
        public int[] Port { get; set; }

        [Parameter(HelpMessage = "Timeout en secondes par port testé (défaut 2).")]
        [ValidateRange(1, 60)]
        public int TimeoutSeconds { get; set; } = 2;

        [Parameter(HelpMessage = "Identifiants alternatifs pour interroger une forêt distante (utilisés uniquement pour la découverte).")]
        [Credential]
        public PSCredential Credential { get; set; } = PSCredential.Empty;

        private readonly IDcInventoryProvider _inventoryProvider = new DcInventoryProvider();
        private readonly IPortHealthProbe _portProbe = new PortHealthProbe(new TcpPortProber());

        protected override void ProcessRecord()
        {
            using (var cts = new CancellationTokenSource())
            {
                IReadOnlyList<DomainControllerInfo> dcs = ResolveDomainControllers(cts.Token);
                if (dcs.Count == 0)
                {
                    WriteWarning("Aucun contrôleur à tester.");
                    return;
                }

                var ports = BuildPortList();
                WriteVerbose($"Test de {ports.Count} port(s) sur {dcs.Count} contrôleur(s)...");

                var probeTask = _portProbe.ProbeAsync(
                    dcs, ports, TimeSpan.FromSeconds(TimeoutSeconds), cts.Token);
                probeTask.Wait(cts.Token);

                foreach (var check in probeTask.Result.Checks)
                {
                    WriteObject(check);
                }
            }
        }

        /// <summary>
        /// Si -DCHostName est fourni, on ne fait pas d'appel AD : on construit
        /// directement la liste à partir des FQDN passés. Permet de tester un
        /// hôte précis sans avoir besoin des credentials de découverte AD.
        /// </summary>
        private IReadOnlyList<DomainControllerInfo> ResolveDomainControllers(CancellationToken token)
        {
            if (DCHostName != null && DCHostName.Length > 0)
            {
                return DCHostName
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => new DomainControllerInfo { HostName = h.Trim() })
                    .ToList();
            }

            var context = AuditContextBuilder.Build(ForestName, Credential);
            WriteVerbose("Découverte des contrôleurs de la forêt...");
            var invTask = _inventoryProvider.GetAllAsync(context, token);
            invTask.Wait(token);
            return invTask.Result;
        }

        /// <summary>Convertit les ports utilisateur en AdServicePort, ou retourne la liste par défaut.</summary>
        private IReadOnlyList<AdServicePort> BuildPortList()
        {
            if (Port == null || Port.Length == 0)
                return AdServicePorts.Default;

            // Pour les ports custom, on met un label générique : le service n'est pas devinable
            // (5985 = WinRM, 53 = DNS, etc. → on évite les heuristiques fragiles).
            return Port.Select(p => new AdServicePort(p, "Custom port " + p)).ToList();
        }
    }
}
