using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les site links avec un coût aberrant. Le coût détermine
    /// la topologie de réplication choisie par le KCC : une valeur invalide
    /// (≤ 0) ou démesurée (&gt; 10000) traduit presque toujours une erreur
    /// de saisie qui fausse les chemins de réplication.
    ///
    /// Convention Microsoft : valeur par défaut 100, plage saine 1-10000.
    /// </summary>
    public sealed class SiteLinkCostDetector : IIssueDetector
    {
        public const int MinSaneCost = 1;
        public const int MaxSaneCost = 10000;

        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.SiteLinks == null) yield break;

            foreach (var link in snapshot.SiteLinks)
            {
                if (link == null || string.IsNullOrEmpty(link.Name)) continue;
                if (link.Cost >= MinSaneCost && link.Cost <= MaxSaneCost) continue;

                yield return new DetectedIssue
                {
                    Code        = "SITE_LINK_COST_ABERRANT",
                    Severity    = IssueSeverity.Warning,
                    Title       = $"Coût aberrant sur le site link {link.Name} ({link.Cost})",
                    Description =
                        "Le coût d'un site link doit être un entier strictement positif, " +
                        $"typiquement entre {MinSaneCost} et {MaxSaneCost} (valeur par défaut 100). " +
                        "Une valeur hors plage indique généralement une erreur de saisie " +
                        "et peut fausser le calcul de topologie réalisé par le KCC.",
                    AffectedItems = new[] { link.Name }
                };
            }
        }
    }
}
