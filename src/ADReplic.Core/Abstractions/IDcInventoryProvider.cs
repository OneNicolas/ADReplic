using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Découverte de l'inventaire des contrôleurs de domaine.
    /// Découplé pour permettre une implémentation alternative en tests (mock).
    /// </summary>
    public interface IDcInventoryProvider
    {
        Task<IReadOnlyList<DomainControllerInfo>> GetAllAsync(
            AuditContext context,
            CancellationToken cancellationToken);
    }
}
