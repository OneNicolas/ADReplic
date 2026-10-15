using System;
using System.Threading;
using ADReplic.Core.Health.Ports;
using ADReplic.Core.Models;

namespace ADReplic.Core.Discovery
{
    /// <summary>
    /// Pré-test de joignabilité d'un DC avant toute lecture DirectoryServices.
    /// Chaque propriété lue sur un DC éteint (site, OS, catalogue global) attend son
    /// timeout LDAP complet : trois lectures coûtaient plusieurs minutes par DC. Un
    /// test TCP sur le port LDAP (389) tranche en deux secondes.
    ///
    /// Seul un port ouvert vaut joignable : un refus actif (RST) signifie que la
    /// machine répond mais que LDAP n'y est pas servi, et les lectures AD
    /// échoueraient de toute façon.
    /// </summary>
    public sealed class DcReachabilityChecker
    {
        public const int LdapPort = 389;
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private readonly IPortProber _prober;

        public DcReachabilityChecker(IPortProber prober)
        {
            _prober = prober ?? throw new ArgumentNullException(nameof(prober));
        }

        /// <summary>Synchrone : appelé depuis le thread de découverte, déjà hors UI.</summary>
        public bool IsReachable(string hostName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(hostName)) return false;
            try
            {
                var outcome = _prober.TryConnectAsync(hostName, LdapPort, Timeout, cancellationToken)
                                     .GetAwaiter().GetResult();
                return outcome != null && outcome.Status == PortCheckStatus.Open;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }
    }
}
