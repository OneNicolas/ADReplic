using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Sonde de santé réseau : teste les ports TCP critiques de chaque DC.
    /// Retourne un PortHealthResult qui sera consommé par AuditSnapshot,
    /// les exports et la GUI.
    /// </summary>
    public interface IPortHealthProbe
    {
        Task<PortHealthResult> ProbeAsync(
            IEnumerable<DomainControllerInfo> domainControllers,
            IReadOnlyList<AdServicePort> portsToCheck,
            TimeSpan perPortTimeout,
            CancellationToken cancellationToken);
    }
}
