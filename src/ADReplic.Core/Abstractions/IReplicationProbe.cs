using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Sonde de réplication : retourne tous les liens entrants pour les DC fournis.
    /// </summary>
    public interface IReplicationProbe
    {
        Task<IReadOnlyList<ReplicationLink>> ProbeAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            AuditContext context,
            CancellationToken cancellationToken);
    }
}
