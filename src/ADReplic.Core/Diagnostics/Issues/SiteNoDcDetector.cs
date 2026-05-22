using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les sites définis dans la topologie mais qui ne contiennent aucun DC.
    /// Pas critique en soi (un site peut être en réserve ou en cours de décommission),
    /// mais ça encombre la console Sites and Services et peut induire en erreur lors
    /// du diagnostic. Sévérité Info.
    /// </summary>
    public sealed class SiteNoDcDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.Sites == null) yield break;

            foreach (var site in snapshot.Sites)
            {
                if (site == null || string.IsNullOrEmpty(site.Name)) continue;
                if (site.DomainControllers != null && site.DomainControllers.Count > 0) continue;

                yield return new DetectedIssue
                {
                    Code        = "SITE_NO_DC",
                    Severity    = IssueSeverity.Info,
                    Title       = $"Site sans contrôleur de domaine : {site.Name}",
                    Description =
                        "Ce site est déclaré mais ne contient aucun DC. S'il n'est pas " +
                        "destiné à recevoir un DC à court terme, envisagez de le supprimer " +
                        "de Sites and Services pour clarifier la topologie.",
                    AffectedItems = new[] { site.Name }
                };
            }
        }
    }
}
