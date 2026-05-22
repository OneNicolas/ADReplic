using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les ports critiques inaccessibles (fermés ou en timeout) sur un DC.
    /// Ports critiques : 389 (LDAP), 88 (Kerberos), 445 (SMB). Sans ces ports,
    /// le DC ne peut pas servir les fonctions essentielles d'AD.
    ///
    /// Timeout traité comme Closed : du point de vue du service, l'effet est le
    /// même (impossible de s'y connecter). La distinction Closed/Timeout reste
    /// utile pour le diagnostic (firewall vs service down) mais pas pour la
    /// catégorisation de la gravité.
    ///
    /// Silencieux si la sonde Ports n'a pas été exécutée.
    /// </summary>
    public sealed class PortCriticalClosedDetector : IIssueDetector
    {
        private static readonly HashSet<int> CriticalPorts = new HashSet<int> { 389, 88, 445 };

        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.PortHealth?.Checks == null) yield break;

            foreach (var check in snapshot.PortHealth.Checks)
            {
                if (!CriticalPorts.Contains(check.Port)) continue;
                if (check.Status != PortCheckStatus.Closed &&
                    check.Status != PortCheckStatus.Timeout) continue;

                var statusLabel = check.Status == PortCheckStatus.Closed ? "fermé" : "sans réponse (timeout)";

                yield return new DetectedIssue
                {
                    Code        = "PORT_CRITICAL_CLOSED",
                    Severity    = IssueSeverity.Critical,
                    Title       = $"Port AD critique inaccessible : {check.HostName}:{check.Port} ({check.ServiceLabel})",
                    Description =
                        $"Le port {check.Port} ({check.ServiceLabel}) du DC {check.HostName} est {statusLabel}. " +
                        "Sans ce port, le DC ne peut pas servir les fonctions essentielles d'AD " +
                        "(authentification, annuaire ou partage SYSVOL). Vérifiez le pare-feu local, " +
                        "le routage réseau et l'état du service correspondant sur le DC.",
                    AffectedItems = new[] { $"{check.HostName}:{check.Port}" }
                };
            }
        }
    }
}
