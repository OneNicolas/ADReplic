using System;
using System.Management.Automation;
using ADReplic.Core.Comparison;
using ADReplic.Core.Export;
using ADReplic.Core.Models;

namespace ADReplic.PowerShell.Cmdlets
{
    /// <summary>
    /// Compare deux exports JSON d'audit (produits par Export-ADRAudit -Format JSON
    /// ou par les snapshots de la surveillance GUI) et retourne le delta complet.
    /// Avec -SignificantOnly, ne retourne que les changements significatifs
    /// (dégradations dignes d'alerte, mêmes 6 critères que la surveillance GUI) :
    /// une sortie vide signifie qu'aucune dégradation notable n'a eu lieu, ce qui
    /// rend le switch directement utilisable en automatisation
    /// (if (Compare-ADRAudit ... -SignificantOnly) { alerte }).
    /// </summary>
    [Cmdlet(VerbsData.Compare, "ADRAudit")]
    [OutputType(typeof(AuditDiff))]
    [OutputType(typeof(SignificantChange))]
    public sealed class CompareAdrAuditCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0,
                   HelpMessage = "Chemin du JSON d'audit de référence (l'état « avant »).")]
        [ValidateNotNullOrEmpty]
        public string ReferencePath { get; set; }

        [Parameter(Mandatory = true, Position = 1,
                   HelpMessage = "Chemin du JSON d'audit courant (l'état « après »).")]
        [ValidateNotNullOrEmpty]
        public string CurrentPath { get; set; }

        [Parameter(HelpMessage = "Ne retourne que les changements significatifs (dégradations). Sortie vide = rien de grave.")]
        public SwitchParameter SignificantOnly { get; set; }

        protected override void ProcessRecord()
        {
            var reader = new JsonAuditReader();
            var reference = ReadSnapshot(reader, ReferencePath);
            var current = ReadSnapshot(reader, CurrentPath);

            var diff = AuditDiffer.Compute(reference, current);

            // Comparer deux forêts différentes est probablement une erreur de
            // manipulation : on prévient sans bloquer (cas légitime : renommage).
            if (!string.Equals(diff.ReferenceForestName, diff.CurrentForestName, StringComparison.OrdinalIgnoreCase))
            {
                WriteWarning(
                    $"Les deux audits ne portent pas sur la même forêt " +
                    $"({diff.ReferenceForestName} vs {diff.CurrentForestName}).");
            }

            if (SignificantOnly.IsPresent)
            {
                foreach (var change in SignificantChangeDetector.Detect(diff))
                {
                    WriteObject(change);
                }
            }
            else
            {
                WriteObject(diff);
            }
        }

        private AuditSnapshot ReadSnapshot(JsonAuditReader reader, string userPath)
        {
            // La résolution est DANS le try : un chemin avec caractères invalides
            // (Path.IsPathRooted lève ArgumentException) doit produire la même
            // erreur terminante propre qu'un fichier introuvable ou corrompu.
            var resolved = userPath;
            try
            {
                resolved = ResolveInputPath(userPath);
                return reader.Read(resolved);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "AuditJsonReadFailed", ErrorCategory.InvalidData, resolved));
                return null; // jamais atteint, ThrowTerminatingError interrompt.
            }
        }

        private string ResolveInputPath(string userPath)
        {
            // Même logique que Export-ADRAudit : les chemins relatifs sont résolus
            // contre la location PowerShell courante, pas le CurrentDirectory du process.
            if (System.IO.Path.IsPathRooted(userPath)) return userPath;
            var providerPath = SessionState.Path.CurrentFileSystemLocation.Path;
            return System.IO.Path.Combine(providerPath, userPath);
        }
    }
}
