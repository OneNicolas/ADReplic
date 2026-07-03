namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Paramètres de surveillance auto-refresh persistés entre sessions.
    /// Volontairement minimaliste : on ne mémorise PAS l'état activé pour
    /// éviter qu'ADReplic se lance avec un audit non sollicité au démarrage.
    /// </summary>
    public sealed class MonitoringSettings
    {
        /// <summary>Intervalle entre deux passes auto, en minutes. Valeurs typiques : 5, 15, 30, 60.</summary>
        public int IntervalMinutes { get; set; } = 15;
    }
}
