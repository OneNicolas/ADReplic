using System;
using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les DC qui n'apparaissent dans aucun lien de réplication
    /// (ni source, ni destination) alors que d'autres DC sont connectés.
    /// Un DC isolé est invisible des autres et son annuaire divergera
    /// jusqu'à atteindre le tombstone lifetime (60 ou 180 jours).
    ///
    /// Garde-fous (pour éviter les faux positifs) :
    /// - inventaire à 1 seul DC → ignoré (SingleDcDomainDetector s'en charge)
    /// - mode DC seul → ignoré (faux positif garanti par construction)
    /// - aucun lien observé → ignoré (la sonde a probablement échoué partout)
    /// </summary>
    public sealed class IsolatedDcDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DomainControllers == null || snapshot.DomainControllers.Count < 2)
                yield break;
            if (snapshot.IsSingleDcMode)
                yield break;
            if (snapshot.ReplicationLinks == null || snapshot.ReplicationLinks.Count == 0)
                yield break;

            var connectedDcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in snapshot.ReplicationLinks)
            {
                if (!string.IsNullOrEmpty(link.SourceDc))      connectedDcs.Add(link.SourceDc);
                if (!string.IsNullOrEmpty(link.DestinationDc)) connectedDcs.Add(link.DestinationDc);
            }

            foreach (var dc in snapshot.DomainControllers)
            {
                if (string.IsNullOrEmpty(dc.HostName)) continue;
                if (connectedDcs.Contains(dc.HostName)) continue;

                yield return new DetectedIssue
                {
                    Code        = "DC_ISOLATED",
                    Severity    = IssueSeverity.Critical,
                    Title       = $"Contrôleur de domaine isolé : {dc.HostName}",
                    Description =
                        "Ce DC n'apparaît dans aucun lien de réplication connu. " +
                        "Vérifiez la connectivité réseau, la résolution DNS et le service NTDS. " +
                        "À défaut, le contenu de son annuaire divergera jusqu'à atteindre " +
                        "le tombstone lifetime (60 ou 180 jours selon l'âge de la forêt).",
                    AffectedItems = new[] { dc.HostName }
                };
            }
        }
    }
}
