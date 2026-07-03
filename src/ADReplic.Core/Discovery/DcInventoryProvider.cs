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
    /// <remarks>
    /// Résilience aux DCs injoignables (v0.5.0) : l'API .NET DirectoryServices fait
    /// un appel réseau au DC visé dès qu'on lit une propriété (SiteName, OSVersion…).
    /// Si UN DC du domaine ne répond pas, une lecture naïve fait planter l'audit complet.
    /// On wrappe donc chaque accès propriété pour garantir que l'énumération continue
    /// et que les DCs joignables apparaissent même si d'autres sont down. Cohérent avec
    /// la stratégie déjà appliquée dans ReplicationProbe et ReplicationFailureProbe.
    /// </remarks>
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

                    // Try/catch défensif : un DC dont les propriétés sont totalement
                    // illisibles ne doit pas faire planter l'énumération des autres.
                    // Le BuildInfo lui-même est tolérant aux propriétés individuelles
                    // injoignables ; ce catch ne se déclenche que si dc.Name plante,
                    // ce qui est très rare.
                    try
                    {
                        list.Add(BuildInfo(dc, domain, forest, fsmoRoles, domainFsmoRoles, discoveredAt));
                    }
                    catch
                    {
                        // Skip ce DC, continue l'audit.
                    }
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
            // dc.Name est typiquement déjà résolu côté client (chargé pendant
            // l'énumération parente), donc accessible même si le DC est down.
            // S'il plantait, l'appelant catch et passe au DC suivant.
            var hostName = dc.Name;

            // Joignabilité : on compte les lectures de propriétés qui font un appel
            // réseau vers CE DC. Le DC est déclaré injoignable seulement si TOUTES
            // échouent : une seule lecture en échec (attribut absent, droit manquant)
            // ne doit pas classer un DC sain en Critical.
            var attemptedDcReads = 0;
            var failedDcReads = 0;
            T TryGetFromDc<T>(Func<T> getter)
            {
                attemptedDcReads++;
                try { return getter(); }
                catch { failedDcReads++; return default(T); }
            }

            var siteName = TryGetFromDc(() => dc.SiteName);
            var osVersion = TryGetFromDc(() => dc.OSVersion);
            var isGlobalCatalog = TryGetFromDc(() => dc.IsGlobalCatalog());

            return new DomainControllerInfo
            {
                HostName = hostName,
                Domain = TryGet(() => domain.Name),
                Forest = TryGet(() => forest.Name),
                SiteName = siteName,
                IPAddress = ResolveIpQuietly(hostName),
                OSVersion = osVersion,
                IsGlobalCatalog = isGlobalCatalog,
                IsReadOnly = IsReadOnlyQuietly(dc),
                IsReachable = failedDcReads < attemptedDcReads,
                Roles = ResolveRoles(hostName, forestFsmoRoles, domainFsmoRoles),
                DiscoveredAt = discoveredAt
            };
        }

        private static IDictionary<string, string> CollectForestFsmoRoles(Forest forest)
        {
            // Chaque accès *RoleOwner contacte le DC qui détient le rôle. Si ce DC est
            // down, on perd l'info pour ce rôle uniquement, sans bloquer le reste.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TryAddRole(map, TryGet(() => forest.SchemaRoleOwner), "SchemaMaster");
            TryAddRole(map, TryGet(() => forest.NamingRoleOwner), "NamingMaster");
            return map;
        }

        private static IDictionary<string, string> CollectDomainFsmoRoles(Domain domain)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TryAddRole(map, TryGet(() => domain.PdcRoleOwner), "PDC");
            TryAddRole(map, TryGet(() => domain.RidRoleOwner), "RID");
            TryAddRole(map, TryGet(() => domain.InfrastructureRoleOwner), "Infrastructure");
            return map;
        }

        private static void TryAddRole(IDictionary<string, string> map, DomainController dc, string role)
        {
            if (dc == null) return;
            var name = TryGet(() => dc.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            if (map.TryGetValue(name, out var existing))
            {
                map[name] = existing + "," + role;
            }
            else
            {
                map[name] = role;
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

        /// <summary>
        /// Wrapper d'accès silencieux pour les propriétés de l'API DirectoryServices
        /// qui font un appel réseau implicite et peuvent lever
        /// ActiveDirectoryServerDownException si le DC visé ne répond pas.
        /// </summary>
        private static T TryGet<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default(T); }
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

        private static bool IsReadOnlyQuietly(DomainController dc)
        {
            // L'API expose Roles : la propriété PartitionType n'est pas dispo en NetFx,
            // on s'appuie sur la convention de nommage RODC (commence par "RODC") ou
            // l'absence de WriteableNC. Pour rester simple : non détecté pour l'instant.
            return false;
        }
    }
}
