using System;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Mise en forme commune des noms d'hôte avant comparaison entre sources :
    /// les cibles SRV issues du DNS portent souvent un point final (FQDN absolu)
    /// que les noms de l'inventaire n'ont pas, et la casse varie selon l'API qui
    /// a fourni le nom.
    /// </summary>
    internal static class HostNameNormalizer
    {
        public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>Nom trimé sans point final, ou null si vide.</summary>
        public static string Normalize(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return null;
            var normalized = host.Trim().TrimEnd('.');
            return normalized.Length == 0 ? null : normalized;
        }

        public static bool AreSame(string left, string right)
        {
            var l = Normalize(left);
            var r = Normalize(right);
            return l != null && r != null && Comparer.Equals(l, r);
        }
    }
}
