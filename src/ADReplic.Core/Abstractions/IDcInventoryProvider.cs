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

        /// <summary>
        /// Construit un inventaire d'un seul DC, sans énumérer toute la forêt.
        /// Utilisé par le mode "audit ciblé" de la GUI.
        /// </summary>
        Task<DomainControllerInfo> GetSingleAsync(
            string dcHostName,
            AuditContext context,
            CancellationToken cancellationToken);
    }
}
