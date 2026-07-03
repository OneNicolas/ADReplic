using System.Management.Automation;
using ADReplic.Core.Models;

namespace ADReplic.PowerShell.Internal
{
    /// <summary>
    /// Construit un AuditContext à partir des paramètres standards des cmdlets ADReplic.
    /// Centralisé pour éviter la duplication entre les cmdlets, et pour garantir
    /// que l'extraction du mot de passe depuis PSCredential reste consistante.
    /// </summary>
    internal static class AuditContextBuilder
    {
        /// <summary>
        /// Surcharge principale : vérifie en plus que si -Credential a été fourni
        /// explicitement par l'utilisateur, il contient réellement des identifiants.
        /// </summary>
        /// <remarks>
        /// Garde-fou 5.1 : [ValidateNotNull] ne suffit pas ici, car la transformation
        /// [Credential] s'exécute AVANT la validation et convertit un $null en valeur
        /// non-nulle — vérifié empiriquement sous Windows PowerShell 5.1. Conséquence
        /// sans ce garde-fou : un Get-Credential annulé ou muet ferait continuer
        /// l'audit silencieusement sous le compte courant, alors que l'utilisateur
        /// croit utiliser des identifiants alternatifs. On préfère une erreur claire.
        /// </remarks>
        public static AuditContext Build(PSCmdlet cmdlet, string forestName, PSCredential credential)
        {
            var explicitlyProvided = cmdlet != null
                && cmdlet.MyInvocation.BoundParameters.ContainsKey("Credential");

            if (explicitlyProvided && IsEmpty(credential))
            {
                cmdlet.ThrowTerminatingError(new ErrorRecord(
                    new PSArgumentException(
                        "-Credential a été fourni mais ne contient aucun identifiant " +
                        "(Get-Credential annulé ou resté muet ?). Fournissez un PSCredential " +
                        "valide, ou omettez le paramètre pour utiliser le compte courant. " +
                        "Fallback si Get-Credential n'affiche rien : " +
                        "$u = Read-Host 'User' ; $p = Read-Host 'Password' -AsSecureString ; " +
                        "$cred = New-Object System.Management.Automation.PSCredential($u, $p)"),
                    "CredentialProvidedButEmpty", ErrorCategory.InvalidArgument, credential));
            }

            return Build(forestName, credential);
        }

        public static AuditContext Build(string forestName, PSCredential credential)
        {
            var context = new AuditContext
            {
                ForestName = string.IsNullOrWhiteSpace(forestName) ? null : forestName.Trim()
            };

            if (!IsEmpty(credential))
            {
                context.Username = credential.UserName;
                // GetNetworkCredential() expose le mot de passe en clair :
                // c'est nécessaire pour DirectoryContext qui prend une string.
                // Le mot de passe ne sort jamais du process Core.
                context.Password = credential.GetNetworkCredential().Password;
            }

            return context;
        }

        private static bool IsEmpty(PSCredential credential)
            => credential == null || credential == PSCredential.Empty;
    }
}
