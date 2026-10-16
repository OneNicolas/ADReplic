using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Découvre la topologie : sites, subnets, liens inter-sites, têtes de pont.
    /// </summary>
    public interface ITopologyProvider
    {
        Task<TopologySnapshot> GetAsync(AuditContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Lit la topologie en se liant à un DC précis, connu joignable (v0.8.0).
        /// Sans cela, le localisateur Windows choisit « un DC » — parfois celui qui
        /// vient d'être éteint — et l'audit s'arrête alors que la découverte a réussi.
        /// </summary>
        Task<TopologySnapshot> GetAsync(AuditContext context, string preferredDcHostName, CancellationToken cancellationToken);
    }

    public sealed class TopologySnapshot
    {
        public IReadOnlyList<SiteInfo> Sites { get; set; }
        public IReadOnlyList<SiteLinkInfo> SiteLinks { get; set; }
    }
}
