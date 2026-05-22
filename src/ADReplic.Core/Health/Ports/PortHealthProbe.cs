using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Ports
{
    /// <summary>
    /// Sonde de santé réseau pour les DC d'une forêt.
    /// Pour chaque couple (DC, port), exécute un test TCP via le prober injecté,
    /// agrège les résultats. Parallélisme élevé (16) parce que ces tests sont
    /// purement I/O bound : pas de bénéfice à les sérialiser.
    /// </summary>
    public sealed class PortHealthProbe : IPortHealthProbe
    {
        private const int DefaultMaxParallelism = 16;
        public static readonly TimeSpan DefaultPerPortTimeout = TimeSpan.FromSeconds(2);

        private readonly IPortProber _prober;
        private readonly int _maxParallelism;

        public PortHealthProbe(IPortProber prober, int maxParallelism = DefaultMaxParallelism)
        {
            if (prober == null) throw new ArgumentNullException(nameof(prober));
            _prober = prober;
            _maxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
        }

        public async Task<PortHealthResult> ProbeAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            IReadOnlyList<AdServicePort> portsToCheck,
            TimeSpan perPortTimeout,
            CancellationToken cancellationToken)
        {
            var dcs = (domainControllers ?? Array.Empty<DomainControllerInfo>())
                .Where(dc => !string.IsNullOrWhiteSpace(dc?.HostName))
                .ToList();

            var ports = portsToCheck ?? AdServicePorts.Default;
            var timeout = perPortTimeout > TimeSpan.Zero ? perPortTimeout : DefaultPerPortTimeout;

            var bag = new ConcurrentBag<PortCheckResult>();
            using (var throttle = new SemaphoreSlim(_maxParallelism))
            {
                var tasks = new List<Task>(dcs.Count * ports.Count);
                foreach (var dc in dcs)
                {
                    foreach (var port in ports)
                    {
                        var capturedDc = dc;
                        var capturedPort = port;
                        tasks.Add(ProbeOneAsync(capturedDc, capturedPort, timeout, bag, throttle, cancellationToken));
                    }
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return new PortHealthResult
            {
                Checks = bag
                    .OrderBy(c => c.HostName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Port)
                    .ToList(),
                CapturedAtUtc = DateTime.UtcNow
            };
        }

        private async Task ProbeOneAsync(
            DomainControllerInfo dc,
            AdServicePort port,
            TimeSpan timeout,
            ConcurrentBag<PortCheckResult> sink,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var outcome = await _prober.TryConnectAsync(
                    dc.HostName, port.Port, timeout, cancellationToken).ConfigureAwait(false);

                sink.Add(new PortCheckResult
                {
                    HostName = dc.HostName,
                    Port = port.Port,
                    ServiceLabel = port.ServiceLabel,
                    Status = outcome.Status,
                    ResponseTime = outcome.ResponseTime,
                    ErrorMessage = outcome.ErrorMessage
                });
            }
            finally
            {
                throttle.Release();
            }
        }
    }
}
