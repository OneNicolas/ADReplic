using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Abstractions;
using ADReplic.Core.Ad;
using ADReplic.Core.Models;

namespace ADReplic.Core.Discovery
{
    /// <summary>
    /// Inventaire via les API natives System.DirectoryServices.ActiveDirectory.
    /// Aucune dépendance RSAT requise sur la machine appelante.
    /// </summary>
    public sealed class DcInventoryProvider : IDcInventoryProvider
    {
        public Task<IReadOnlyList<DomainControllerInfo>> GetAllAsync(
            AuditContext context,
            CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Les API DS sont synchrones bloquantes : on délègue à un thread du pool
            // pour que la UI thread reste fluide pendant la découverte.
            return Task.Run<IReadOnlyList<DomainControllerInfo>>(
                () => Discover(context, cancellationToken),
                cancellationToken);
        }

        public Task<DomainControllerInfo> GetSingleAsync(
            string dcHostName,
            AuditContext context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dcHostName))
                throw new ArgumentException("Nom de DC requis.", nameof(dcHostName));
            if (context == null) throw new ArgumentNullException(nameof(context));

            return Task.Run(() => DiscoverSingle(dcHostName, context, cancellationToken), cancellationToken);
        }

        private static IReadOnlyList<DomainControllerInfo> Discover(
            AuditContext context,
            CancellationToken cancellationToken)
        {
            var forest = DirectoryContextFactory.OpenForest(context);
            var fsmoRoles = CollectForestFsmoRoles(forest);

            var discoveredAt = DateTime.Now;
            var list = new List<DomainControllerInfo>();

            foreach (Domain domain in forest.Domains)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var domainFsmoRoles = CollectDomainFsmoRoles(domain);

                foreach (DomainController dc in domain.DomainControllers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    list.Add(BuildInfo(dc, domain, forest, fsmoRoles, domainFsmoRoles, discoveredAt));
                }
            }

            return list;
        }

        private static DomainControllerInfo DiscoverSingle(
            string dcHostName,
            AuditContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Forest + rôles FSMO globaux sont toujours collectés, on en a besoin pour
            // déterminer si le DC visé détient un rôle (PDC, RID...). Le coût est minime
            // (lecture des partitions Configuration) et garantit que les exports
            // restent fidèles même en mode ciblé.
            var forest = DirectoryContextFactory.OpenForest(context);
            var forestFsmoRoles = CollectForestFsmoRoles(forest);

            var serverContext = DirectoryContextFactory.CreateServerContext(context, dcHostName);
            var dc = DomainController.GetDomainController(serverContext);

            var domain = dc.Domain;
            var domainFsmoRoles = CollectDomainFsmoRoles(domain);

            cancellationToken.ThrowIfCancellationRequested();
            return BuildInfo(dc, domain, forest, forestFsmoRoles, domainFsmoRoles, DateTime.Now);
        }

        private static DomainControllerInfo BuildInfo(
            DomainController dc,
            Domain domain,
            Forest forest,
            IDictionary<string, string> forestFsmoRoles,
            IDictionary<string, string> domainFsmoRoles,
            DateTime discoveredAt)
        {
            return new DomainControllerInfo
            {
                HostName = dc.Name,
                Domain = domain.Name,
                Forest = forest.Name,
                SiteName = dc.SiteName,
                IPAddress = ResolveIpQuietly(dc.Name),
                OSVersion = dc.OSVersion,
                IsGlobalCatalog = IsGlobalCatalogQuietly(dc),
                IsReadOnly = IsReadOnlyQuietly(dc),
                Roles = ResolveRoles(dc.Name, forestFsmoRoles, domainFsmoRoles),
                DiscoveredAt = discoveredAt
            };
        }

        private static IDictionary<string, string> CollectForestFsmoRoles(Forest forest)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TryAddRole(map, forest.SchemaRoleOwner, "SchemaMaster");
            TryAddRole(map, forest.NamingRoleOwner, "NamingMaster");
            return map;
        }

        private static IDictionary<string, string> CollectDomainFsmoRoles(Domain domain)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TryAddRole(map, domain.PdcRoleOwner, "PDC");
            TryAddRole(map, domain.RidRoleOwner, "RID");
            TryAddRole(map, domain.InfrastructureRoleOwner, "Infrastructure");
            return map;
        }

        private static void TryAddRole(IDictionary<string, string> map, DomainController dc, string role)
        {
            if (dc == null || string.IsNullOrWhiteSpace(dc.Name)) return;
            if (map.TryGetValue(dc.Name, out var existing))
            {
                map[dc.Name] = existing + "," + role;
            }
            else
            {
                map[dc.Name] = role;
            }
        }

        private static string[] ResolveRoles(
            string hostName,
            IDictionary<string, string> forestRoles,
            IDictionary<string, string> domainRoles)
        {
            var collected = new List<string>();
            if (forestRoles.TryGetValue(hostName, out var fRoles)) collected.AddRange(fRoles.Split(','));
            if (domainRoles.TryGetValue(hostName, out var dRoles)) collected.AddRange(dRoles.Split(','));
            return collected.Count == 0 ? Array.Empty<string>() : collected.ToArray();
        }

        private static string ResolveIpQuietly(string hostName)
        {
            try
            {
                var addresses = Dns.GetHostAddresses(hostName);
                var v4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return (v4 ?? addresses.FirstOrDefault())?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsGlobalCatalogQuietly(DomainController dc)
        {
            try { return dc.IsGlobalCatalog(); } catch { return false; }
        }

        private static bool IsReadOnlyQuietly(DomainController dc)
        {
            // L'API expose Roles : la propriété PartitionType n'est pas dispo en NetFx,
            // on s'appuie sur la convention de nommage RODC (commence par "RODC") ou
            // l'absence de WriteableNC. Pour rester simple : non détecté pour l'instant.
            return false;
        }
    }
}
