using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les DC déclarés dans l'annuaire mais injoignables au moment de
    /// l'audit (flag <see cref="DomainControllerInfo.IsReachable"/> positionné
    /// par la découverte quand toutes les lectures réseau vers le DC échouent).
    ///
    /// C'est l'anomalie "chapeau" du scénario DC down : elle nomme explicitement
    /// le DC en cause, là où les autres signaux (ports en timeout, ligne
    /// Unreachable dans la matrice) restent indirects. ObsoleteOsDetector se tait
    /// volontairement sur ces DCs pour ne pas doubler d'un OS_UNKNOWN trompeur.
    /// </summary>
    public sealed class DcUnreachableDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DomainControllers == null) yield break;

            foreach (var dc in snapshot.DomainControllers)
            {
                if (dc == null || string.IsNullOrEmpty(dc.HostName)) continue;
                if (dc.IsReachable) continue;

                yield return new DetectedIssue
                {
                    Code        = "DC_UNREACHABLE",
                    Severity    = IssueSeverity.Critical,
                    Title       = $"Contrôleur de domaine injoignable : {dc.HostName}",
                    Description =
                        "Ce DC est déclaré dans l'annuaire mais n'a répondu à aucune " +
                        "interrogation pendant l'audit. Il est probablement éteint, isolé " +
                        "du réseau, ou son service AD DS (NTDS) est arrêté. Vérifiez l'état " +
                        "de la machine et sa connectivité. S'il a été retiré définitivement " +
                        "sans rétrogradation propre, nettoyez ses métadonnées " +
                        "(metadata cleanup) pour éviter les SRV orphelins et les échecs " +
                        "de réplication persistants.",
                    AffectedItems = new[] { dc.HostName }
                };
            }
        }
    }
}
