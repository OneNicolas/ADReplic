using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Clock
{
    /// <summary>
    /// Sonde d'horloge (v0.8.0) : lit l'heure de chaque DC joignable et mesure son
    /// décalage par rapport au poste d'audit. Le détecteur DC_CLOCK_SKEW compare
    /// ensuite ces décalages ENTRE DC, ce qui annule l'horloge du poste d'audit.
    /// </summary>
    public sealed class ClockHealthProbe
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
        private const int MaxParallelism = 8;

        private readonly IDcClockReader _reader;
        private readonly TimeSpan _timeout;

        public ClockHealthProbe(IDcClockReader reader, TimeSpan? timeout = null)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _timeout = timeout ?? DefaultTimeout;
        }

        public async Task<ClockHealthResult> ProbeAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            CancellationToken cancellationToken)
        {
            var dcs = (domainControllers ?? Array.Empty<DomainControllerInfo>())
                .Where(dc => !string.IsNullOrWhiteSpace(dc?.HostName))
                .ToList();

            var bag = new ConcurrentBag<ClockCheckResult>();
            using (var throttle = new SemaphoreSlim(MaxParallelism))
            {
                await Task.WhenAll(dcs.Select(dc => ProbeOneAsync(dc, bag, throttle, cancellationToken)))
                          .ConfigureAwait(false);
            }

            return new ClockHealthResult
            {
                ProbedAtUtc = DateTime.UtcNow,
                Checks = bag.OrderBy(c => c.HostName, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private async Task ProbeOneAsync(
            DomainControllerInfo dc, ConcurrentBag<ClockCheckResult> sink,
            SemaphoreSlim throttle, CancellationToken cancellationToken)
        {
            // Déjà tranché par le pré-test de la découverte : inutile d'attendre un timeout LDAP de plus.
            if (!dc.IsReachable)
            {
                sink.Add(new ClockCheckResult { HostName = dc.HostName, Status = ClockCheckStatus.Skipped, ObservedAtUtc = DateTime.UtcNow, Detail = "DC injoignable : non interrogé." });
                return;
            }

            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTime? dcTime;
                try
                {
                    dcTime = await _reader.ReadCurrentTimeUtcAsync(dc.HostName, _timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    sink.Add(new ClockCheckResult { HostName = dc.HostName, Status = ClockCheckStatus.Timeout, ObservedAtUtc = DateTime.UtcNow, Detail = "Pas de réponse LDAP dans le délai." });
                    return;
                }
                catch (Exception ex)
                {
                    sink.Add(new ClockCheckResult { HostName = dc.HostName, Status = ClockCheckStatus.Error, ObservedAtUtc = DateTime.UtcNow, Detail = ex.Message });
                    return;
                }

                var observedAt = DateTime.UtcNow;
                if (dcTime == null)
                {
                    sink.Add(new ClockCheckResult { HostName = dc.HostName, Status = ClockCheckStatus.Error, ObservedAtUtc = observedAt, Detail = "currentTime absent ou illisible dans le RootDSE." });
                    return;
                }

                sink.Add(new ClockCheckResult
                {
                    HostName = dc.HostName,
                    Status = ClockCheckStatus.Ok,
                    DcTimeUtc = dcTime,
                    ObservedAtUtc = observedAt,
                    OffsetFromAuditHost = dcTime.Value - observedAt
                });
            }
            finally
            {
                throttle.Release();
            }
        }
    }
}
