using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les domaines qui ne contiennent qu'un seul DC. Un domaine mono-DC
    /// est un SPOF total : pas de réplication, pas de tolérance de panne,
    /// pas de balance de charge sur les authentifications.
    /// Une issue est émise par domaine concerné, en listant le DC unique.
    ///
    /// Garde-fou : ignoré en mode DC seul, où l'inventaire à 1 DC est attendu
    /// par construction et ne reflète pas l'état réel du domaine.
    /// </summary>
    public sealed class SingleDcDomainDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DomainControllers == null || snapshot.DomainControllers.Count == 0)
                yield break;
            if (snapshot.IsSingleDcMode)
                yield break;

            var byDomain = snapshot.DomainControllers
                .Where(dc => dc != null && !string.IsNullOrEmpty(dc.Domain))
                .GroupBy(dc => dc.Domain, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byDomain)
            {
                var dcs = group.ToList();
                if (dcs.Count != 1) continue;

                var loneDc = dcs[0].HostName ?? "(inconnu)";
                yield return new DetectedIssue
                {
                    Code        = "DOMAIN_SINGLE_DC",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Domaine {group.Key} avec un seul contrôleur ({loneDc})",
                    Description =
                        "Ce domaine ne contient qu'un seul DC : sa perte rend le domaine " +
                        "indisponible (authentification, GPO, DNS si intégré AD). Ajoutez " +
                        "au moins un deuxième DC pour assurer la redondance.",
                    AffectedItems = new[] { loneDc }
                };
            }
        }
    }
}
