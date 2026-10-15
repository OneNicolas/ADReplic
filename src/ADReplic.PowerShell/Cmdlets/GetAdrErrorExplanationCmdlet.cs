using System.Linq;
using System.Management.Automation;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Models;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Explique un code d'erreur de réplication en français clair : titre, causes,
    /// vérifications et commandes. Hors ligne, sans aucun appel AD. Accepte un code
    /// brut (-Code 1722) ou un échec issu de Get-ADRReplicationFailure via le pipeline.
    /// Sans paramètre : liste tout le catalogue.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "ADRErrorExplanation", DefaultParameterSetName = "ByCode")]
    [OutputType(typeof(ReplicationErrorExplanation))]
    public sealed class GetAdrErrorExplanationCmdlet : PSCmdlet
    {
        [Parameter(ParameterSetName = "ByCode", Position = 0,
                   HelpMessage = "Code d'erreur Win32 / HRESULT signe (ex : 1722, 8453, -2146893022). Plusieurs valeurs possibles.")]
        public int[] Code { get; set; }

        [Parameter(ParameterSetName = "ByFailure", ValueFromPipeline = true,
                   HelpMessage = "Echec de replication (sortie de Get-ADRReplicationFailure).")]
        [ValidateNotNull]
        public ReplicationFailureInfo Failure { get; set; }

        protected override void ProcessRecord()
        {
            if (ParameterSetName == "ByFailure")
            {
                WriteObject(Failure.Explanation);
                return;
            }

            if (Code == null || Code.Length == 0)
            {
                foreach (var code in ReplicationErrorCatalog.KnownCodes.OrderBy(c => c))
                    WriteObject(ReplicationErrorCatalog.Lookup(code));
                return;
            }

            foreach (var code in Code)
                WriteObject(ReplicationErrorCatalog.Lookup(code));
        }
    }
}
