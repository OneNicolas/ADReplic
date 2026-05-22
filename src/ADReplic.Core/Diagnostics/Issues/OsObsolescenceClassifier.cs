using System.Text.RegularExpressions;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Classifie la chaîne <c>OSVersion</c> d'un contrôleur de domaine selon
    /// l'état de support Microsoft (mai 2026). Isolé du détecteur pour rester
    /// testable indépendamment, et ajustable au fil des fins de support.
    ///
    /// Décisions :
    /// - Server 2003 / 2003 R2 / 2008 / 2008 R2 → Critical (fin de support étendu &lt; 2021)
    /// - Server 2012 / 2012 R2                  → Warning  (fin de support étendu oct. 2023)
    /// - Server 2016, 2019, 2022, 2025          → None     (encore supportés)
    /// - Vide ou non reconnu                    → Unknown  (Info, mention informative)
    /// </summary>
    public static class OsObsolescenceClassifier
    {
        // L'ordre compte : on cherche les "R2" avant les versions sans suffixe.
        private static readonly Regex Server2003 = new Regex(@"Windows\s+Server\s+2003", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Server2008 = new Regex(@"Windows\s+Server\s+2008", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Server2012 = new Regex(@"Windows\s+Server\s+2012", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Server2016Plus = new Regex(@"Windows\s+Server\s+(2016|2019|2022|2025)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static OsObsolescenceLevel Classify(string osVersion)
        {
            if (string.IsNullOrWhiteSpace(osVersion)) return OsObsolescenceLevel.Unknown;

            // Ordre du plus récent au plus ancien : on sort dès qu'un match récent réussit.
            if (Server2016Plus.IsMatch(osVersion)) return OsObsolescenceLevel.None;
            if (Server2012.IsMatch(osVersion))     return OsObsolescenceLevel.OutOfExtendedSupport;
            if (Server2008.IsMatch(osVersion))     return OsObsolescenceLevel.UnsupportedCritical;
            if (Server2003.IsMatch(osVersion))     return OsObsolescenceLevel.UnsupportedCritical;

            return OsObsolescenceLevel.Unknown;
        }
    }

    public enum OsObsolescenceLevel
    {
        /// <summary>OS encore supporté par Microsoft.</summary>
        None = 0,

        /// <summary>OSVersion vide ou format non reconnu.</summary>
        Unknown = 1,

        /// <summary>Hors support étendu mais ESU possible (2012 / 2012 R2).</summary>
        OutOfExtendedSupport = 2,

        /// <summary>Plus aucun support, même payant (2003, 2008, 2008 R2).</summary>
        UnsupportedCritical = 3
    }
}
