using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les sites qui contiennent des DC mais aucun serveur tête de pont
    /// déclaré, dans une forêt multi-sites. En l'absence de bridgehead explicite,
    /// l'ISTG en désigne un automatiquement, mais l'absence durable peut signaler
    /// une configuration partielle ou une suppression accidentelle.
    ///
    /// Garde-fou : on n'émet rien si la forêt n'a qu'un seul site (pas de
    /// réplication inter-sites possible, le bridgehead n'a aucun sens).
    /// </summary>
    public sealed class SiteNoBridgeheadDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.Sites == null || snapshot.Sites.Count <= 1) yield break;

            foreach (var site in snapshot.Sites)
            {
                if (site == null || string.IsNullOrEmpty(site.Name)) continue;
                if (site.DomainControllers == null || site.DomainControllers.Count == 0) continue;
                if (site.BridgeheadServers != null && site.BridgeheadServers.Count > 0) continue;

                yield return new DetectedIssue
                {
                    Code        = "SITE_NO_BRIDGEHEAD",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Site sans serveur tête de pont : {site.Name}",
                    Description =
                        "Ce site contient des DC mais aucun bridgehead déclaré. L'ISTG en " +
                        "sélectionne un automatiquement, mais une absence durable peut " +
                        "indiquer une configuration incomplète ou une suppression " +
                        "accidentelle dans Sites and Services.",
                    AffectedItems = new[] { site.Name }
                };
            }
        }
    }
}
