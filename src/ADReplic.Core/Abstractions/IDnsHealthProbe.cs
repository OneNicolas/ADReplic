using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Sonde de santé DNS : vérifie les SRV AD critiques + les A records des DC.
    /// Retourne un DnsHealthResult qui sera consommé par AuditSnapshot, les
    /// exports et la GUI.
    /// </summary>
    public interface IDnsHealthProbe
    {
        Task<DnsHealthResult> ProbeAsync(
            string forestDnsName,
            string primaryDomainDnsName,
            IEnumerable<DomainControllerInfo> domainControllers,
            CancellationToken cancellationToken);
    }
}
