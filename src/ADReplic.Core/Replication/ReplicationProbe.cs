using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Ad;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Models;

namespace ADReplic.Core.Replication
{
    /// <summary>
    /// Sonde de réplication appuyée sur System.DirectoryServices.ActiveDirectory.
    /// Pour chaque DC, GetReplicationNeighbors() retourne ses voisins entrants par NC.
    /// Parallélisé pour rester rapide sur des forêts larges.
    /// </summary>
    public sealed class ReplicationProbe : IReplicationProbe
    {
        private const int DefaultMaxParallelism = 8;

        private readonly int _maxParallelism;

        public ReplicationProbe(int maxParallelism = DefaultMaxParallelism)
        {
            if (maxParallelism < 1) maxParallelism = 1;
            _maxParallelism = maxParallelism;
        }

        public async Task<IReadOnlyList<ReplicationLink>> ProbeAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            AuditContext context,
            CancellationToken cancellationToken)
        {
            if (domainControllers == null) throw new ArgumentNullException(nameof(domainControllers));

            var bag = new ConcurrentBag<ReplicationLink>();
            using (var throttle = new SemaphoreSlim(_maxParallelism))
            {
                var tasks = domainControllers.Select(dc => ProbeOneAsync(dc, context, bag, throttle, cancellationToken));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return bag
                .OrderBy(l => l.DestinationDc)
                .ThenBy(l => l.SourceDc)
                .ThenBy(l => l.NamingContext)
                .ToList();
        }

        private static async Task ProbeOneAsync(
            DomainControllerInfo dc,
            AuditContext context,
            ConcurrentBag<ReplicationLink> sink,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await Task.Run(() => ProbeDcSync(dc, context, sink), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }

        private static void ProbeDcSync(DomainControllerInfo dcInfo, AuditContext context, ConcurrentBag<ReplicationLink> sink)
        {
            DomainController dc = null;
            try
            {
                var ctx = DirectoryContextFactory.CreateServerContext(context, dcInfo.HostName);
                dc = DomainController.GetDomainController(ctx);

                var neighbors = dc.GetAllReplicationNeighbors();
                if (neighbors == null) return;

                foreach (ReplicationNeighbor n in neighbors)
                {
                    sink.Add(BuildLink(dcInfo, n));
                }
            }
            catch (Exception ex)
            {
                // Le DC est injoignable : on émet quand même une ligne pour qu'il apparaisse
                // en rouge dans la matrice plutôt que d'être silencieusement absent.
                sink.Add(new ReplicationLink
                {
                    DestinationDc = dcInfo.HostName,
                    SourceDc = "(inconnu)",
                    NamingContext = "(non interrogé)",
                    LastResultMessage = ex.Message,
                    Status = ReplicationLinkStatus.Unreachable
                });
            }
            finally
            {
                dc?.Dispose();
            }
        }

        private static ReplicationLink BuildLink(DomainControllerInfo destination, ReplicationNeighbor n)
        {
            var lastAttempt = NormalizeDateTime(n.LastAttemptedSync);
            var lastSuccess = NormalizeDateTime(n.LastSuccessfulSync);

            var link = new ReplicationLink
            {
                DestinationDc = destination.HostName,
                SourceDc = DistinguishedNameHelper.ExtractServerName(n.SourceServer),
                NamingContext = n.PartitionName,
                PartitionType = PartitionClassifier.Classify(n.PartitionName),
                IsIntraSite = IsIntraSiteFlag(n),
                LastAttempt = lastAttempt,
                LastSuccess = lastSuccess,
                ConsecutiveFailures = n.ConsecutiveFailureCount,
                LastResultCode = n.LastSyncResult,
                LastResultMessage = ResolveMessage(n.LastSyncMessage, n.LastSyncResult),
            };

            link.Status = ReplicationStatusEvaluator.Evaluate(
                link.ConsecutiveFailures,
                link.LastResultCode,
                link.LastAttempt,
                link.LastSuccess);

            return link;
        }

        private static DateTime? NormalizeDateTime(DateTime value)
        {
            // L'API retourne DateTime.MinValue quand l'événement n'a jamais eu lieu.
            return value > new DateTime(1601, 1, 2) ? value : (DateTime?)null;
        }

        private static string ResolveMessage(string apiMessage, int resultCode)
        {
            // Priorité au message fourni par l'API ; fallback sur la traduction du HRESULT.
            if (!string.IsNullOrWhiteSpace(apiMessage)) return apiMessage;
            return Win32ErrorMessage.Resolve(resultCode);
        }

        private static bool IsIntraSiteFlag(ReplicationNeighbor n)
        {
            // L'enum est imbriquée dans ReplicationNeighbor : on qualifie complètement.
            // CompressChanges est typiquement positionné pour les liens inter-sites.
            try
            {
                return !n.ReplicationNeighborOption.HasFlag(
                    ReplicationNeighbor.ReplicationNeighborOptions.CompressChanges);
            }
            catch
            {
                return true;
            }
        }
    }
}
