namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Sévérité d'une anomalie de configuration ou de structure détectée
    /// lors de l'analyse d'un audit. Distincte de <c>HealthLevel</c> qui agrège
    /// le score global, et de <c>ReplicationFailureSeverity</c> qui qualifie
    /// la durée d'un échec actif.
    /// </summary>
    public enum IssueSeverity
    {
        /// <summary>Information : pas d'impact immédiat, à connaître.</summary>
        Info = 0,

        /// <summary>Avertissement : configuration sous-optimale ou risque latent.</summary>
        Warning = 1,

        /// <summary>Critique : impact opérationnel direct, à corriger rapidement.</summary>
        Critical = 2
    }
}
