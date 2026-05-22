using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les ports AD secondaires inaccessibles (Closed ou Timeout) sur un DC.
    /// Concerne les ports en dehors du triplet critique 389/88/445 :
    /// DNS (53), RPC EPM (135), kpasswd (464), LDAPS (636), GC (3268), GC LDAPS (3269).
    ///
    /// Leur indisponibilité dégrade certaines fonctionnalités sans bloquer le
    /// fonctionnement de base d'AD, d'où la sévérité Warning.
    ///
    /// Silencieux si la sonde Ports n'a pas été exécutée.
    /// </summary>
    public sealed class PortClosedOrFilteredDetector : IIssueDetector
    {
        private static readonly HashSet<int> CriticalPorts = new HashSet<int> { 389, 88, 445 };

        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.PortHealth?.Checks == null) yield break;

            foreach (var check in snapshot.PortHealth.Checks)
            {
                // Les ports critiques sont déjà couverts par PortCriticalClosedDetector.
                if (CriticalPorts.Contains(check.Port)) continue;
                if (check.Status != PortCheckStatus.Closed &&
                    check.Status != PortCheckStatus.Timeout) continue;

                var statusLabel = check.Status == PortCheckStatus.Closed ? "fermé" : "sans réponse (timeout)";

                yield return new DetectedIssue
                {
                    Code        = "PORT_CLOSED_OR_FILTERED",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Port AD secondaire inaccessible : {check.HostName}:{check.Port} ({check.ServiceLabel})",
                    Description =
                        $"Le port {check.Port} ({check.ServiceLabel}) du DC {check.HostName} est {statusLabel}. " +
                        "Le DC reste fonctionnel pour les opérations essentielles, mais certaines " +
                        "fonctionnalités peuvent être dégradées (résolution DNS croisée, LDAPS, " +
                        "interrogations sur catalogue global). Vérifiez pare-feu et routage si la " +
                        "fonctionnalité concernée est utilisée.",
                    AffectedItems = new[] { $"{check.HostName}:{check.Port}" }
                };
            }
        }
    }
}
