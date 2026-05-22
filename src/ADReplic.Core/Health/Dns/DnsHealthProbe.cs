using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Dns
{
    /// <summary>
    /// Sonde de santé DNS pour une forêt AD.
    /// Compose la liste des noms à vérifier (4 SRV + 1 A par DC), exécute les
    /// requêtes en parallèle via le résolveur injecté, puis assemble un
    /// DnsHealthResult prêt à être consommé par le snapshot et les exports.
    ///
    /// Le résolveur est injecté pour rendre la classe testable : en production
    /// on injecte Win32DnsResolver ; en tests on injecte un fake déterministe.
    /// </summary>
    public sealed class DnsHealthProbe : IDnsHealthProbe
    {
        private const int DefaultMaxParallelism = 8;

        private readonly IDnsResolver _resolver;
        private readonly int _maxParallelism;

        public DnsHealthProbe(IDnsResolver resolver, int maxParallelism = DefaultMaxParallelism)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            _resolver = resolver;
            _maxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
        }

        public async Task<DnsHealthResult> ProbeAsync(
            string forestDnsName,
            string primaryDomainDnsName,
            IEnumerable<DomainControllerInfo> domainControllers,
            CancellationToken cancellationToken)
        {
            var dcList = (domainControllers ?? Array.Empty<DomainControllerInfo>()).ToList();
            var srvQueries = DnsSrvNames.Build(forestDnsName, primaryDomainDnsName);

            var bag = new ConcurrentBag<DnsCheckResult>();
            using (var throttle = new SemaphoreSlim(_maxParallelism))
            {
                var tasks = new List<Task>();

                foreach (var srv in srvQueries)
                {
                    var capturedSrv = srv;
                    tasks.Add(RunQueryAsync(
                        () => QuerySrv(capturedSrv),
                        bag, throttle, cancellationToken));
                }

                foreach (var dc in dcList)
                {
                    if (string.IsNullOrWhiteSpace(dc?.HostName)) continue;
                    var capturedHost = dc.HostName;
                    tasks.Add(RunQueryAsync(
                        () => QueryA(capturedHost),
                        bag, throttle, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return new DnsHealthResult
            {
                Checks = bag
                    .OrderBy(c => (int)c.Type)
                    .ThenBy(c => c.RecordName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Target, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                CapturedAtUtc = DateTime.UtcNow
            };
        }

        // -- Lancement parallèle --------------------------------------------------

        private static async Task RunQueryAsync(
            Func<IEnumerable<DnsCheckResult>> queryFn,
            ConcurrentBag<DnsCheckResult> sink,
            SemaphoreSlim throttle,
            CancellationToken cancellationToken)
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    foreach (var r in queryFn()) sink.Add(r);
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }

        // -- Conversion résultat brut -> DnsCheckResult ---------------------------

        private IEnumerable<DnsCheckResult> QuerySrv(DnsSrvQuery srv)
        {
            var raw = _resolver.Query(srv.Name, DnsQueryRecordType.Srv);
            return BuildCheckResults(srv.Name, srv.Category, raw, FillSrvFields);
        }

        private IEnumerable<DnsCheckResult> QueryA(string host)
        {
            var raw = _resolver.Query(host, DnsQueryRecordType.A);
            return BuildCheckResults(host, DnsCheckedRecordType.ARecord, raw, FillAFields);
        }

        private static IEnumerable<DnsCheckResult> BuildCheckResults(
            string recordName,
            DnsCheckedRecordType category,
            DnsQueryResult raw,
            Action<DnsCheckResult, DnsRawRecord> fillTypedFields)
        {
            // En cas de Missing ou Error, on émet UNE ligne récapitulative pour
            // que la GUI/CSV ait toujours une trace par requête (pas de "trou silencieux").
            if (raw.Status != DnsQueryStatus.Ok || raw.Records == null || raw.Records.Count == 0)
            {
                yield return new DnsCheckResult
                {
                    RecordName = recordName,
                    Type = category,
                    Status = MapStatus(raw.Status),
                    ErrorCode = raw.ErrorCode,
                    ErrorMessage = ResolveErrorMessage(raw.ErrorCode)
                };
                yield break;
            }

            foreach (var rec in raw.Records)
            {
                var result = new DnsCheckResult
                {
                    RecordName = recordName,
                    Type = category,
                    Status = DnsCheckStatus.Ok,
                    ErrorCode = 0,
                };
                // Le fill method s'occupe de Target ET des champs typés ; chaque type
                // de record décide ce que "Target" signifie pour lui (cible SRV vs nom interrogé pour A).
                fillTypedFields(result, rec);
                yield return result;
            }
        }

        private static void FillSrvFields(DnsCheckResult result, DnsRawRecord rec)
        {
            // Pour un SRV, la cible vient du RDATA (pNameTarget côté Win32).
            result.Target = rec.Target;
            result.Port = rec.Port;
            result.Priority = rec.Priority;
            result.Weight = rec.Weight;
        }

        private static void FillAFields(DnsCheckResult result, DnsRawRecord rec)
        {
            // Le A record ne porte pas de champ "target" dans son RDATA : par
            // convention on affiche le nom interrogé (celui dont on a obtenu une IP).
            result.Target = result.RecordName;
            result.IpAddress = rec.IpAddress;
        }

        private static DnsCheckStatus MapStatus(DnsQueryStatus status)
        {
            switch (status)
            {
                case DnsQueryStatus.Ok: return DnsCheckStatus.Ok;
                case DnsQueryStatus.Missing: return DnsCheckStatus.Missing;
                default: return DnsCheckStatus.Error;
            }
        }

        private static string ResolveErrorMessage(int errorCode)
        {
            if (errorCode == 0) return null;
            // Win32ErrorMessage utilise FormatMessage qui couvre les DNS_ERROR_* (9000+)
            // au même titre que les codes Win32 classiques.
            return Win32ErrorMessage.ResolveOrCode(errorCode);
        }
    }
}
