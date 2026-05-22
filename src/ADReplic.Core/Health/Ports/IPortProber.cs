using System;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Ports
{
    /// <summary>
    /// Abstraction d'un testeur de port TCP, isolée de l'API socket pour permettre
    /// le mock en tests. Toute la mécanique TcpClient/Task.WhenAny vit dans
    /// l'implémentation concrète ; le reste du Core consomme cette interface.
    /// </summary>
    public interface IPortProber
    {
        /// <summary>
        /// Tente d'ouvrir une connexion TCP sur (host, port) avec le délai donné.
        /// Ne lève pas en cas d'échec de connexion : un port fermé ou un timeout
        /// est un résultat normal, pas une exception. Lève en revanche en cas de
        /// CancellationToken déclenché.
        /// </summary>
        Task<PortProbeOutcome> TryConnectAsync(
            string hostName,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Résultat brut d'un test de port, sans information métier (libellé service, etc.).
    /// Le mapping vers PortCheckResult est fait par PortHealthProbe.
    /// </summary>
    public sealed class PortProbeOutcome
    {
        public PortCheckStatus Status { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string ErrorMessage { get; set; }
    }
}
