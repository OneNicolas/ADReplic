using System.Management.Automation;
using ADReplic.Core.Models;

namespace ADReplic.PowerShell.Internal
{
    /// <summary>
    /// Construit un AuditContext à partir des paramètres standards des cmdlets ADReplic.
    /// Centralisé pour éviter la duplication entre les 5 cmdlets, et pour garantir
    /// que l'extraction du mot de passe depuis PSCredential reste consistante.
    /// </summary>
    internal static class AuditContextBuilder
    {
        public static AuditContext Build(string forestName, PSCredential credential)
        {
            var context = new AuditContext
            {
                ForestName = string.IsNullOrWhiteSpace(forestName) ? null : forestName.Trim()
            };

            if (credential != null && credential != PSCredential.Empty)
            {
                context.Username = credential.UserName;
                // GetNetworkCredential() expose le mot de passe en clair :
                // c'est nécessaire pour DirectoryContext qui prend une string.
                // Le mot de passe ne sort jamais du process Core.
                context.Password = credential.GetNetworkCredential().Password;
            }

            return context;
        }
    }
}
