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

        /// <summary>
        /// Dernière forêt cible auditée (v0.9.0), rechargée au lancement pour retrouver
        /// l'entrée du coffre d'identifiants correspondante. Null = forêt courante.
        /// Jamais de mot de passe ici : le secret vit dans le Gestionnaire d'identifiants.
        /// </summary>
        public string LastTargetForest { get; set; }
    }
}
