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
    }

    public sealed class TopologySnapshot
    {
        public IReadOnlyList<SiteInfo> Sites { get; set; }
        public IReadOnlyList<SiteLinkInfo> SiteLinks { get; set; }
    }
}
