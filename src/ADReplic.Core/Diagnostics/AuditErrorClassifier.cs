using System;
using System.DirectoryServices.ActiveDirectory;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Transforme une exception remontée par les sondes AD en un message
    /// utilisateur orienté action (vérifier le DNS, le format UPN, etc.).
    /// Les exceptions natives sont génériques ("Logon failure") et masquent
    /// souvent la vraie cause environnementale.
    /// </summary>
    public static class AuditErrorClassifier
    {
        public static AuditErrorCategory Classify(Exception ex)
        {
            if (ex == null) return AuditErrorCategory.Unknown;

            // Cas spécifiques typés en premier.
            if (ex is ActiveDirectoryServerDownException) return AuditErrorCategory.ServerDown;
            if (ex is ActiveDirectoryObjectNotFoundException) return AuditErrorCategory.ForestOrObjectNotFound;

            var message = ex.Message ?? string.Empty;
            var lower = message.ToLowerInvariant();

            // L'authentification échoue : code 1326 (LogonFailure) ou texte explicite.
            // Le message peut être localisé, on couvre FR+EN.
            if (lower.Contains("logon failure") ||
                lower.Contains("user name or password") ||
                lower.Contains("nom d'utilisateur") ||
                lower.Contains("mot de passe") ||
                lower.Contains("ouverture de session") ||
                lower.Contains("1326"))
            {
                return AuditErrorCategory.AuthenticationFailed;
            }

            // RPC down, port LDAP fermé : code 1722.
            if (lower.Contains("1722") || lower.Contains("rpc server is unavailable"))
            {
                return AuditErrorCategory.ServerDown;
            }

            // Accès refusé sur l'objet AD (l'auth a marché mais le compte n'a pas les droits).
            if (lower.Contains("access is denied") ||
                lower.Contains("accès refusé") ||
                lower.Contains("acces refuse"))
            {
                return AuditErrorCategory.AccessDenied;
            }

            return AuditErrorCategory.Unknown;
        }

        public static string Explain(Exception ex)
        {
            switch (Classify(ex))
            {
                case AuditErrorCategory.AuthenticationFailed:
                    return "Identifiants refusés par le contrôleur de domaine. " +
                           "Vérifie le format (essaie 'utilisateur@domaine.complet' ou 'DOMAINE\\utilisateur') " +
                           "et que le suffixe correspond bien au domaine cible.";

                case AuditErrorCategory.ServerDown:
                    return "Contrôleur de domaine injoignable. " +
                           "Vérifie que ton DNS pointe vers un DC du domaine cible " +
                           "(nslookup -type=SRV _ldap._tcp.<domaine>) et que les ports 389/445/3268 sont ouverts.";

                case AuditErrorCategory.ForestOrObjectNotFound:
                    return "Forêt ou objet introuvable. " +
                           "Vérifie le nom de la forêt cible et la résolution DNS depuis cette machine.";

                case AuditErrorCategory.AccessDenied:
                    return "Accès refusé sur l'objet AD. " +
                           "L'authentification a réussi mais ce compte n'a pas les droits de lecture sur la cible.";

                default:
                    return ex?.Message ?? "Erreur inconnue.";
            }
        }
    }

    public enum AuditErrorCategory
    {
        Unknown,
        AuthenticationFailed,
        ServerDown,
        ForestOrObjectNotFound,
        AccessDenied
    }
}
