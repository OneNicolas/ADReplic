using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les enregistrements SRV DNS secondaires manquants (_gc, _kpasswd).
    /// Leur absence dégrade certaines fonctionnalités (recherche dans le catalogue
    /// global, changement de mot de passe Kerberos) sans bloquer entièrement la forêt.
    ///
    /// Silencieux si la sonde DNS n'a pas été exécutée.
    /// </summary>
    public sealed class DnsSrvOptionalMissingDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DnsHealth?.Checks == null) yield break;

            foreach (var check in snapshot.DnsHealth.Checks)
            {
                if (check.Status != DnsCheckStatus.Missing) continue;
                if (check.Type != DnsCheckedRecordType.SrvGc &&
                    check.Type != DnsCheckedRecordType.SrvKpasswd) continue;

                yield return new DetectedIssue
                {
                    Code        = "DNS_SRV_OPTIONAL_MISSING",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Enregistrement SRV DNS secondaire introuvable : {check.RecordName}",
                    Description =
                        "Cet enregistrement SRV n'est pas indispensable au fonctionnement principal " +
                        "de la forêt, mais son absence peut dégrader certaines fonctionnalités " +
                        "(catalogue global ou changement de mot de passe Kerberos). " +
                        "Vérifiez la publication par les DC concernés.",
                    AffectedItems = new[] { check.RecordName }
                };
            }
        }
    }
}
