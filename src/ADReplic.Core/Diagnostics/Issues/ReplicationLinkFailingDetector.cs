using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Signale les liens de réplication en statut <see cref="ReplicationLinkStatus.Failing"/>
    /// (≥ 3 échecs consécutifs ou plus de 3 h sans succès, cf. ReplicationStatusEvaluator),
    /// regroupés par couple destination ← source.
    ///
    /// Pourquoi Failing seul : le statut Warning couvre les rattrapages normaux après
    /// un redémarrage de DC (latence 30 min – 3 h) ; les remonter ici dupliquerait
    /// l'onglet Réplication et noierait les vrais incidents. Dès le premier succès,
    /// un lien Failing redevient Healthy : pas de fenêtre de faux positif au retour.
    ///
    /// Pourquoi ignorer les DC injoignables : DC_UNREACHABLE nomme déjà la cause ;
    /// répéter « réplication en échec » pour chacun de ses partenaires n'apporte rien.
    /// </summary>
    public sealed class ReplicationLinkFailingDetector : IIssueDetector
    {
        public const string Code = "REPLICATION_LINK_FAILING";

        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.ReplicationLinks == null) yield break;

            var unreachableHosts = CollectUnreachableHosts(snapshot.DomainControllers);

            var failingLinks = snapshot.ReplicationLinks
                .Where(link => link != null && link.Status == ReplicationLinkStatus.Failing)
                .Where(link => !string.IsNullOrWhiteSpace(link.DestinationDc) && !string.IsNullOrWhiteSpace(link.SourceDc))
                .Where(link => !unreachableHosts.Contains(HostNameNormalizer.Normalize(link.DestinationDc)))
                .Where(link => !unreachableHosts.Contains(HostNameNormalizer.Normalize(link.SourceDc)));

            var pairs = failingLinks.GroupBy(
                link => new PairKey(link.DestinationDc, link.SourceDc),
                PairKey.Comparer);

            foreach (var pair in pairs)
            {
                yield return BuildIssue(pair.Key, pair.ToList());
            }
        }

        private static HashSet<string> CollectUnreachableHosts(IReadOnlyList<DomainControllerInfo> dcs)
        {
            var hosts = new HashSet<string>(HostNameNormalizer.Comparer);
            if (dcs == null) return hosts;

            foreach (var dc in dcs)
            {
                if (dc == null || dc.IsReachable) continue;
                var host = HostNameNormalizer.Normalize(dc.HostName);
                if (host != null) hosts.Add(host);
            }
            return hosts;
        }

        private static DetectedIssue BuildIssue(PairKey pair, List<ReplicationLink> links)
        {
            var partitions = links.Select(l => l.NamingContext).Where(nc => !string.IsNullOrWhiteSpace(nc)).ToList();
            var oldestOutage = links.Select(l => l.Latency).Where(l => l.HasValue).OrderByDescending(l => l.Value).FirstOrDefault();
            var neverSucceeded = links.Any(l => !l.LastSuccess.HasValue);
            var maxFailures = links.Max(l => l.ConsecutiveFailures);
            var lastError = links.Where(l => l.LastResultCode != 0).OrderByDescending(l => l.LastAttempt ?? DateTime.MinValue).FirstOrDefault();

            var partitionLabel = partitions.Count == 1 ? "1 partition" : $"{partitions.Count} partitions";
            var sinceLabel = neverSucceeded
                ? "jamais répliqué avec succès"
                : $"sans succès depuis {DurationFormatter.ToHumanText(oldestOutage)}";

            return new DetectedIssue
            {
                Code          = Code,
                Severity      = IssueSeverity.Critical,
                Title         = $"Réplication en échec : {pair.Destination} ← {pair.Source} ({partitionLabel}, {sinceLabel})",
                Description   = BuildDescription(pair, partitions, maxFailures, lastError),
                AffectedItems = new[] { pair.Destination, pair.Source },
                RelatedHost   = pair.Destination
            };
        }

        private static string BuildDescription(PairKey pair, List<string> partitions, int maxFailures, ReplicationLink lastError)
        {
            var text =
                $"{pair.Destination} n'arrive plus à recevoir les modifications de {pair.Source}. " +
                $"Tentatives consécutives en échec : {maxFailures}. ";

            if (lastError != null)
            {
                // Les messages Win32 se terminent souvent par un retour à la ligne :
                // sans Trim, « (code N) » passe à la ligne suivante dans la fiche.
                var message = string.IsNullOrWhiteSpace(lastError.LastResultMessage)
                    ? Win32ErrorMessage.ResolveOrCode(lastError.LastResultCode)
                    : lastError.LastResultMessage.Trim();
                text += $"Dernière erreur : {message} (code {lastError.LastResultCode}). ";
            }

            text += "Partitions concernées : " + string.Join(", ", partitions) + ". " +
                    "Les objets modifiés sur la source (mots de passe, comptes, GPO) ne sont pas visibles " +
                    "sur la destination tant que ce lien n'est pas rétabli.";
            return text;
        }

        /// <summary>Clé de regroupement destination ← source, insensible à la casse et au point final.</summary>
        private sealed class PairKey
        {
            public static readonly IEqualityComparer<PairKey> Comparer = new PairKeyComparer();

            public PairKey(string destination, string source)
            {
                Destination = destination.Trim();
                Source = source.Trim();
            }

            public string Destination { get; }
            public string Source { get; }

            private sealed class PairKeyComparer : IEqualityComparer<PairKey>
            {
                public bool Equals(PairKey x, PairKey y) =>
                    HostNameNormalizer.AreSame(x.Destination, y.Destination)
                    && HostNameNormalizer.AreSame(x.Source, y.Source);

                public int GetHashCode(PairKey key) =>
                    HostNameNormalizer.Comparer.GetHashCode(HostNameNormalizer.Normalize(key.Destination) ?? "")
                    ^ HostNameNormalizer.Comparer.GetHashCode(HostNameNormalizer.Normalize(key.Source) ?? "");
            }
        }
    }
}
