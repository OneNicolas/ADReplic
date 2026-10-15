using System.Collections.Generic;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Fiche explicative d'un code d'erreur de réplication, destinée à un
    /// lecteur non expert : ce que l'erreur veut dire, pourquoi elle arrive,
    /// quoi vérifier et quelles commandes lancer. Les commandes sont fournies
    /// pour être copiées, jamais exécutées par ADReplic.
    /// </summary>
    public sealed class ReplicationErrorExplanation
    {
        /// <summary>Code Win32 / HRESULT signé tel que remonté par AD.</summary>
        public int Code { get; set; }

        /// <summary>Titre court en français clair (une ligne).</summary>
        public string Title { get; set; }

        /// <summary>Ce que l'erreur signifie concrètement, sans jargon.</summary>
        public string Explanation { get; set; }

        /// <summary>Causes les plus fréquentes, de la plus probable à la moins probable.</summary>
        public IReadOnlyList<string> ProbableCauses { get; set; } = new List<string>();

        /// <summary>Vérifications à mener, dans l'ordre, du plus simple au plus intrusif.</summary>
        public IReadOnlyList<string> Checks { get; set; } = new List<string>();

        /// <summary>Commandes prêtes à copier (repadmin, dcdiag, nltest...).</summary>
        public IReadOnlyList<string> Commands { get; set; } = new List<string>();

        /// <summary>Article Microsoft Learn de référence, ou null.</summary>
        public string DocumentationUrl { get; set; }

        /// <summary>Vrai si le code figure dans le catalogue (sinon fiche générique).</summary>
        public bool IsKnown { get; set; }

        /// <summary>
        /// Vrai pour un état transitoire normal (ex : 8477, réplication en cours)
        /// qui ne doit pas être présenté comme une panne.
        /// </summary>
        public bool IsTransient { get; set; }

        /// <summary>
        /// Vrai quand l'erreur signale un risque de perte ou d'incohérence de
        /// données (objets persistants, tombstone dépassé) : à traiter en priorité.
        /// </summary>
        public bool IsDataLossRisk { get; set; }

        public override string ToString() => $"{Code} - {Title}";
    }
}
