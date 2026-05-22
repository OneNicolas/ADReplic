using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les DC dont le A record est manquant dans le DNS.
    /// Un DC sans A record résolvable est fonctionnellement injoignable :
    /// les clients ne peuvent pas se connecter à lui même si les SRV pointent
    /// vers son nom.
    ///
    /// Silencieux si la sonde DNS n'a pas été exécutée.
    /// </summary>
    public sealed class DnsDcARecordMissingDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DnsHealth?.Checks == null) yield break;

            foreach (var check in snapshot.DnsHealth.Checks)
            {
                if (check.Type != DnsCheckedRecordType.ARecord) continue;
                if (check.Status != DnsCheckStatus.Missing) continue;

                yield return new DetectedIssue
                {
                    Code        = "DNS_DC_A_RECORD_MISSING",
                    Severity    = IssueSeverity.Critical,
                    Title       = $"Contrôleur de domaine non résolvable : {check.RecordName}",
                    Description =
                        "Le nom du DC ne se résout vers aucune adresse IPv4 dans le DNS interrogé. " +
                        "Les clients ne pourront pas se connecter à ce DC même si les SRV pointent " +
                        "vers son nom. Vérifiez l'enregistrement A dans la zone DNS et le service " +
                        "Net Logon sur le DC concerné.",
                    AffectedItems = new[] { check.RecordName }
                };
            }
        }
    }
}
