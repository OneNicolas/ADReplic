using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Ad;
using ADReplic.Core.Models;
using ADReplic.Core.Replication;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Récupère les échecs de réplication en cours via DomainController.GetReplicationConnectionFailures().
    /// Parallélisé comme la sonde principale.
    /// </summary>
    public sealed class ReplicationFailureProbe : IReplicationFailureProbe
    {
        private const int DefaultMaxParallelism = 8;
        private readonly int _maxParallelism;

        public ReplicationFailureProbe(int maxParallelism = DefaultMaxParallelism)
        {
            _maxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
        }

        public async Task<IReadOnlyList<ReplicationFailureInfo>> GetFailuresAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            AuditContext context,
            CancellationToken cancellationToken)
        {
            if (domainControllers == null) throw new ArgumentNullException(nameof(domainControllers));

            var bag = new ConcurrentBag<ReplicationFailureInfo>();
            using (var throttle = new SemaphoreSlim(_maxParallelism))
            {
                var tasks = domainControllers.Select(dc => ProbeOneAsync(dc, context, bag, throttle, cancellationToken));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return bag
                .OrderByDescending(f => f.ConsecutiveFailureCount)
                .ThenBy(f => f.DestinationDc)
                .ThenBy(f => f.SourceDc)
                .ToList();
        }

        private static async Task ProbeOneAsync(
            DomainControllerInfo dc,
            AuditContext context,
            ConcurrentBag<ReplicationFailureInfo> sink,
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

        private static void ProbeDcSync(DomainControllerInfo dcInfo, AuditContext context, ConcurrentBag<ReplicationFailureInfo> sink)
        {
            DomainController dc = null;
            try
            {
                var ctx = DirectoryContextFactory.CreateServerContext(context, dcInfo.HostName);
                dc = DomainController.GetDomainController(ctx);

                var failures = dc.GetReplicationConnectionFailures();
                if (failures == null) return;

                foreach (ReplicationFailure f in failures)
                {
                    sink.Add(BuildFailure(dcInfo, f));
                }
            }
            catch (Exception ex)
            {
                // DC injoignable : on émet un échec "synthétique" pour le signaler.
                sink.Add(new ReplicationFailureInfo
                {
                    DestinationDc = dcInfo.HostName,
                    SourceDc = "(injoignable)",
                    FirstFailureTime = DateTime.Now,
                    ConsecutiveFailureCount = 0,
                    LastErrorCode = -1,
                    LastErrorMessage = "Impossible d'interroger le DC : " + ex.Message,
                    Severity = ReplicationFailureSeverity.Critical
                });
            }
            finally
            {
                dc?.Dispose();
            }
        }

        private static ReplicationFailureInfo BuildFailure(DomainControllerInfo destination, ReplicationFailure f)
        {
            var info = new ReplicationFailureInfo
            {
                DestinationDc = destination.HostName,
                SourceDc = DistinguishedNameHelper.ExtractServerName(f.SourceServer),
                FirstFailureTime = f.FirstFailureTime,
                ConsecutiveFailureCount = f.ConsecutiveFailureCount,
                LastErrorCode = f.LastErrorCode,
                LastErrorMessage = Win32ErrorMessage.ResolveOrCode(f.LastErrorCode)
            };
            info.Severity = FailureSeverityClassifier.Classify(info.FailureDuration);
            return info;
        }
    }
}
