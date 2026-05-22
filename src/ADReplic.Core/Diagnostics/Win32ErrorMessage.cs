using System;
using System.ComponentModel;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Traduit un HRESULT / code d'erreur Win32 en message lisible.
    /// Utilise FormatMessage en interne (localisé selon l'OS).
    /// </summary>
    public static class Win32ErrorMessage
    {
        /// <summary>
        /// Retourne le message système pour le code donné, ou null si inconnu.
        /// </summary>
        public static string Resolve(int errorCode)
        {
            if (errorCode == 0) return null;

            try
            {
                var message = new Win32Exception(errorCode).Message;
                // Win32Exception renvoie "Unknown error (0x...)" quand le code est inconnu :
                // on filtre pour ne pas polluer l'UI avec ce texte peu utile.
                if (string.IsNullOrEmpty(message)) return null;
                if (message.StartsWith("Unknown error", StringComparison.OrdinalIgnoreCase)) return null;
                return message.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Variante qui renvoie toujours quelque chose d'affichable :
        /// le message si trouvé, sinon "Code 0x… (inconnu)".
        /// </summary>
        public static string ResolveOrCode(int errorCode)
        {
            if (errorCode == 0) return null;
            var msg = Resolve(errorCode);
            if (!string.IsNullOrEmpty(msg)) return msg;
            return $"Code 0x{errorCode:X8} ({errorCode})";
        }
    }
}
