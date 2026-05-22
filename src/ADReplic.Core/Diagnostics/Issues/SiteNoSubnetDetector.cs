using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les sites qui contiennent des DC mais n'ont aucun subnet associé.
    /// Sans subnet, les clients ne peuvent pas se localiser sur ce site via DCLocator,
    /// et seront authentifiés par n'importe quel DC de la forêt — typiquement via
    /// un lien WAN, avec une latence qui dégrade l'expérience utilisateur et
    /// surcharge inutilement les liens inter-sites.
    /// </summary>
    public sealed class SiteNoSubnetDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.Sites == null) yield break;

            foreach (var site in snapshot.Sites)
            {
                if (site == null || string.IsNullOrEmpty(site.Name)) continue;
                if (site.DomainControllers == null || site.DomainControllers.Count == 0) continue;
                if (site.Subnets != null && site.Subnets.Count > 0) continue;

                yield return new DetectedIssue
                {
                    Code        = "SITE_NO_SUBNET",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Site sans subnet associé : {site.Name}",
                    Description =
                        "Ce site contient des DC mais aucun subnet IP ne lui est rattaché. " +
                        "Les clients ne pourront pas se localiser sur ce site : DCLocator " +
                        "les enverra sur n'importe quel DC de la forêt, généralement via " +
                        "le WAN. Déclarez les sous-réseaux IP du site dans Sites and Services.",
                    AffectedItems = new[] { site.Name }
                };
            }
        }
    }
}
