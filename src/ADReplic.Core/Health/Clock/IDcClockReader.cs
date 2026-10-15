using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADReplic.Core.Health.Clock
{
    /// <summary>Lit l'heure UTC d'un DC. Null si le DC ne répond pas ou répond n'importe quoi.</summary>
    public interface IDcClockReader
    {
        Task<DateTime?> ReadCurrentTimeUtcAsync(string hostName, TimeSpan timeout, CancellationToken cancellationToken);
    }
}
