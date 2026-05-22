using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les enregistrements SRV DNS critiques manquants (_ldap, _kerberos).
    /// Sans ces SRV, les clients ne savent pas localiser de DC ni d'authentifieur :
    /// la forêt est fonctionnellement injoignable.
    ///
    /// Reste silencieux si la sonde DNS n'a pas été exécutée (DnsHealth == null) :
    /// évite les faux positifs sur les audits "réplication seule".
    /// </summary>
    public sealed class DnsSrvCriticalMissingDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DnsHealth?.Checks == null) yield break;

            foreach (var check in snapshot.DnsHealth.Checks)
            {
                if (check.Status != DnsCheckStatus.Missing) continue;
                if (check.Type != DnsCheckedRecordType.SrvLdap &&
                    check.Type != DnsCheckedRecordType.SrvKerberos) continue;

                yield return new DetectedIssue
                {
                    Code        = "DNS_SRV_CRITICAL_MISSING",
                    Severity    = IssueSeverity.Critical,
                    Title       = $"Enregistrement SRV DNS critique introuvable : {check.RecordName}",
                    Description =
                        "Cet enregistrement SRV permet aux clients de localiser un service AD essentiel " +
                        "(authentification Kerberos ou annuaire LDAP). Son absence empêche le bon " +
                        "fonctionnement de la forêt. Vérifiez la zone DNS du domaine et la bonne " +
                        "publication des SRV par les DC concernés (Net Logon ou DcDiag /test:dns).",
                    AffectedItems = new[] { check.RecordName }
                };
            }
        }
    }
}
