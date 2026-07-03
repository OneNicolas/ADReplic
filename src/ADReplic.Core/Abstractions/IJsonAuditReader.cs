using ADReplic.Core.Models;

namespace ADReplic.Core.Abstractions
{
    /// <summary>
    /// Contrat de lecture d'un AuditSnapshot persisté en JSON.
    /// Symétrique de <see cref="IAuditExporter"/> pour le format JSON.
    /// </summary>
    public interface IJsonAuditReader
    {
        /// <summary>
        /// Lit le fichier JSON et reconstruit un AuditSnapshot. Lève si le
        /// fichier est absent ou si le format est invalide.
        /// </summary>
        AuditSnapshot Read(string path);
    }
}
