using System;

namespace ADReplic.Core.Replication
{
    /// <summary>
    /// Helpers pour les Distinguished Names AD.
    /// </summary>
    public static class DistinguishedNameHelper
    {
        /// <summary>
        /// Extrait le nom du serveur depuis un DN de type
        /// "CN=NTDS Settings,CN=DC01,CN=Servers,CN=Site,CN=Sites,...".
        /// Retourne la chaîne d'origine si le motif n'est pas reconnu.
        /// </summary>
        public static string ExtractServerName(string sourceServerDn)
        {
            if (string.IsNullOrWhiteSpace(sourceServerDn)) return sourceServerDn;

            var parts = sourceServerDn.Split(',');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Trim().Equals("CN=NTDS Settings", StringComparison.OrdinalIgnoreCase))
                {
                    var next = parts[i + 1].Trim();
                    if (next.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                        return next.Substring(3);
                }
            }
            return sourceServerDn;
        }
    }
}
