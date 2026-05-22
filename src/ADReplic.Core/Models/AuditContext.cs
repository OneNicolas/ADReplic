using System;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Identifie la cible AD interrogée et les credentials à utiliser.
    /// - ForestName vide = forêt courante de la machine.
    /// - Username/Password vides = credentials du processus courant (Kerberos intégré).
    ///
    /// Les credentials sont stockés en string en clair pour simplicité. Ils ne
    /// quittent jamais la mémoire du process (jamais sérialisés, jamais exportés).
    /// </summary>
    public sealed class AuditContext
    {
        public string ForestName { get; set; }
        public string PreferredDcHostName { get; set; }
        public TimeSpan PerCallTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public string Username { get; set; }
        public string Password { get; set; }

        public bool HasAlternateCredentials =>
            !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);

        public static AuditContext CurrentForest() => new AuditContext();
    }
}
