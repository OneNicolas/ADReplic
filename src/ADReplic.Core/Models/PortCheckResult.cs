using System;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Résultat d'un test de connectivité TCP sur un port AD d'un DC.
    /// Une instance par couple (DC, port testé).
    /// </summary>
    public sealed class PortCheckResult
    {
        /// <summary>Nom d'hôte testé (ex: dc01.exemple.local).</summary>
        public string HostName { get; set; }

        /// <summary>Numéro de port TCP (ex: 389).</summary>
        public int Port { get; set; }

        /// <summary>Libellé fonctionnel du service (ex: "LDAP"). Sert d'affichage UI.</summary>
        public string ServiceLabel { get; set; }

        /// <summary>Verdict consolidé du test.</summary>
        public PortCheckStatus Status { get; set; }

        /// <summary>Temps de réponse mesuré (utile pour repérer un port lent même s'il répond).</summary>
        public TimeSpan ResponseTime { get; set; }

        /// <summary>Détail d'erreur pour Status = Error. Null sinon.</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Verdict d'un test de port TCP.
    /// - Open : connexion établie dans le délai imparti.
    /// - Closed : refus actif (RST) — service joignable mais port fermé.
    /// - Timeout : aucune réponse dans le délai — typiquement filtré par pare-feu
    ///   ou hôte injoignable. Distinct de Closed pour faciliter le diagnostic.
    /// - Error : autre exception (résolution DNS, droits, etc.).
    /// </summary>
    public enum PortCheckStatus
    {
        Open = 0,
        Closed = 1,
        Timeout = 2,
        Error = 3
    }
}
