using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Ports
{
    /// <summary>
    /// Testeur de port adossé à System.Net.Sockets.TcpClient.
    /// La gestion du timeout passe par Task.WhenAny(connect, Task.Delay) parce que
    /// TcpClient.ConnectAsync(host, port) ne prend pas de CancellationToken en
    /// .NET Framework 4.8. En cas de timeout, on dispose le client : la connexion
    /// en cours est interrompue ; l'exception alors levée par connectTask est
    /// observée silencieusement pour ne pas polluer UnobservedTaskException.
    /// </summary>
    public sealed class TcpPortProber : IPortProber
    {
        public async Task<PortProbeOutcome> TryConnectAsync(
            string hostName,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(hostName, port);
                var timeoutTask = Task.Delay(timeout, cancellationToken);

                var winner = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

                if (winner != connectTask)
                {
                    // Soit timeout, soit cancellation externe : on tranche en regardant le token.
                    cancellationToken.ThrowIfCancellationRequested();
                    ObserveSilently(connectTask);
                    return new PortProbeOutcome
                    {
                        Status = PortCheckStatus.Timeout,
                        ResponseTime = stopwatch.Elapsed
                    };
                }

                // La tâche de connexion est terminée : succès ou exception synchronisée.
                try
                {
                    await connectTask.ConfigureAwait(false);
                    return new PortProbeOutcome
                    {
                        Status = PortCheckStatus.Open,
                        ResponseTime = stopwatch.Elapsed
                    };
                }
                catch (SocketException)
                {
                    // Refus actif (RST) ou échec réseau identifié comme tel : on
                    // distingue ce cas du Timeout pour aider au diagnostic.
                    return new PortProbeOutcome
                    {
                        Status = PortCheckStatus.Closed,
                        ResponseTime = stopwatch.Elapsed
                    };
                }
                catch (Exception ex)
                {
                    return new PortProbeOutcome
                    {
                        Status = PortCheckStatus.Error,
                        ResponseTime = stopwatch.Elapsed,
                        ErrorMessage = ex.Message
                    };
                }
            }
            finally
            {
                // Dispose force la fermeture du socket même si la connexion est encore
                // en cours (cas du timeout).
                client.Dispose();
            }
        }

        /// <summary>
        /// Évite qu'une exception non observée du connectTask (dispose pendant connexion)
        /// ne remonte plus tard dans UnobservedTaskException. On ne fait rien de l'erreur.
        /// </summary>
        private static void ObserveSilently(Task task)
        {
            task.ContinueWith(t =>
            {
                var _ = t.Exception;
            }, TaskScheduler.Default);
        }
    }
}
