namespace ADReplic.Core.Security
{
    /// <summary>Identifiant mémorisé : nom de compte et mot de passe en clair, le temps de l'utiliser.</summary>
    public sealed class StoredCredential
    {
        public StoredCredential(string userName, string password)
        {
            UserName = userName;
            Password = password;
        }

        public string UserName { get; }
        public string Password { get; }
    }

    /// <summary>
    /// Coffre d'identifiants (v0.9.0). L'implémentation Windows s'appuie sur le
    /// Gestionnaire d'identifiants (DPAPI, portée session utilisateur) : l'entrée est
    /// visible et supprimable par l'utilisateur dans le Panneau de configuration,
    /// comme celles mémorisées par RDP. Jamais exporté, jamais dans un snapshot.
    /// </summary>
    public interface ICredentialStore
    {
        bool TryRead(string target, out StoredCredential credential);
        void Save(string target, string userName, string password);
        bool Delete(string target);
    }

    /// <summary>Nom des entrées du coffre : une par forêt cible, insensible à la casse et au point final.</summary>
    public static class CredentialTargets
    {
        public const string Prefix = "ADReplic:";
        private const string CurrentForestKey = "(foret-courante)";

        public static string ForForest(string forestName)
        {
            var normalized = string.IsNullOrWhiteSpace(forestName) ? null : forestName.Trim().TrimEnd('.').ToLowerInvariant();
            return Prefix + (string.IsNullOrEmpty(normalized) ? CurrentForestKey : normalized);
        }
    }
}
