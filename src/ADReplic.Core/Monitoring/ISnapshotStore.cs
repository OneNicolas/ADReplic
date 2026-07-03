using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Contrat d'un magasin de snapshots persistés sur disque. Abstrait pour
    /// permettre aux tests d'injecter une implémentation sans I/O.
    /// </summary>
    public interface ISnapshotStore
    {
        /// <summary>Répertoire racine où les snapshots sont écrits.</summary>
        string BaseDirectory { get; }

        /// <summary>
        /// Écrit le snapshot sur disque (JSON UTF-8) puis applique la rotation
        /// configurée. Retourne le chemin du fichier créé.
        /// </summary>
        string Save(AuditSnapshot snapshot);

        /// <summary>
        /// Liste les snapshots persistés, du plus récent au plus ancien.
        /// Filtre les fichiers ne respectant pas la convention de nommage.
        /// </summary>
        IReadOnlyList<SnapshotFileInfo> List();

        /// <summary>
        /// Charge un snapshot par nom de fichier (résolu dans BaseDirectory)
        /// ou par chemin absolu.
        /// </summary>
        AuditSnapshot Load(string fileNameOrPath);
    }
}
