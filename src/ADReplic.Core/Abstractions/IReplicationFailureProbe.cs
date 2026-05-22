using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Sonde dédiée aux échecs de réplication en cours.
    /// Plus rapide qu'une analyse complète : interroge GetAllReplicationFailures().
    /// </summary>
    public interface IReplicationFailureProbe
    {
        Task<IReadOnlyList<ReplicationFailureInfo>> GetFailuresAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            AuditContext context,
            CancellationToken cancellationToken);
    }
}
