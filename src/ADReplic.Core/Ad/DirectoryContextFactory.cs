using System;
using System.DirectoryServices.ActiveDirectory;
using ADReplic.Core.Models;

namespace ADReplic.Core.Ad
{
    /// <summary>
    /// Construit des DirectoryContext en respectant les credentials de l'AuditContext.
    /// Toute sonde qui parle à AD passe par ce factory pour garantir la cohérence
    /// (un seul endroit qui sait comment mapper AuditContext → DirectoryContext).
    ///
    /// Note : namespace 'Ad' (et non 'Directory') pour éviter la collision avec
    /// System.IO.Directory dans les fichiers qui utilisent les deux.
    /// </summary>
    public static class DirectoryContextFactory
    {
        /// <summary>
        /// Contexte forêt : utilisé pour Forest.GetForest() et l'énumération initiale.
        /// </summary>
        public static DirectoryContext CreateForestContext(AuditContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (string.IsNullOrWhiteSpace(context.ForestName))
            {
                // Forêt courante : DirectoryContext sans nom utilise la forêt du process.
                return context.HasAlternateCredentials
                    ? new DirectoryContext(DirectoryContextType.Forest, context.Username, context.Password)
                    : new DirectoryContext(DirectoryContextType.Forest);
            }

            return context.HasAlternateCredentials
                ? new DirectoryContext(DirectoryContextType.Forest, context.ForestName, context.Username, context.Password)
                : new DirectoryContext(DirectoryContextType.Forest, context.ForestName);
        }

        /// <summary>
        /// Contexte serveur : utilisé pour viser un DC précis (GetReplicationNeighbors, etc.).
        /// </summary>
        public static DirectoryContext CreateServerContext(AuditContext context, string dcHostName)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(dcHostName))
                throw new ArgumentException("DC hostname requis.", nameof(dcHostName));

            return context.HasAlternateCredentials
                ? new DirectoryContext(DirectoryContextType.DirectoryServer, dcHostName, context.Username, context.Password)
                : new DirectoryContext(DirectoryContextType.DirectoryServer, dcHostName);
        }

        /// <summary>
        /// Ouvre la forêt désignée par l'AuditContext.
        /// Cas simple (forêt courante, creds du process) : Forest.GetCurrentForest().
        /// Tout autre cas : Forest.GetForest(context).
        /// </summary>
        public static Forest OpenForest(AuditContext context)
        {
            var noForestName = string.IsNullOrWhiteSpace(context?.ForestName);
            var noCreds = context == null || !context.HasAlternateCredentials;

            if (noForestName && noCreds) return Forest.GetCurrentForest();

            return Forest.GetForest(CreateForestContext(context));
        }
    }
}
