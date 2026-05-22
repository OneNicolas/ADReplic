using System.Collections.Generic;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les erreurs de résolution DNS (Status = Error) qui indiquent un
    /// problème d'infrastructure DNS plutôt qu'un record absent : SERVFAIL,
    /// serveur DNS injoignable, timeout. Distinct de Missing qui signifie
    /// "le record n'existe pas".
    ///
    /// Silencieux si la sonde DNS n'a pas été exécutée.
    /// </summary>
    public sealed class DnsResolutionErrorDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DnsHealth?.Checks == null) yield break;

            foreach (var check in snapshot.DnsHealth.Checks)
            {
                if (check.Status != DnsCheckStatus.Error) continue;

                var win32Message = Win32ErrorMessage.ResolveOrCode(check.ErrorCode);

                yield return new DetectedIssue
                {
                    Code        = "DNS_RESOLUTION_ERROR",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Erreur de résolution DNS : {check.RecordName}",
                    Description =
                        $"La résolution DNS a échoué pour cet enregistrement (code {check.ErrorCode}" +
                        (string.IsNullOrEmpty(win32Message) ? "" : $" — {win32Message}") +
                        "). Cela suggère un souci d'infrastructure DNS (serveur injoignable, " +
                        "SERVFAIL, timeout) plutôt qu'un record absent. Vérifiez la santé des " +
                        "serveurs DNS et leur configuration de forwarders.",
                    AffectedItems = new[] { check.RecordName }
                };
            }
        }
    }
}
