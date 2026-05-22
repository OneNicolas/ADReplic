using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecte les DC dont la version de Windows Server est hors support.
    /// Le parsing de la chaîne <c>OSVersion</c> est délégué à
    /// <see cref="OsObsolescenceClassifier"/> pour rester testable séparément.
    /// </summary>
    public sealed class ObsoleteOsDetector : IIssueDetector
    {
        public IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot)
        {
            if (snapshot?.DomainControllers == null) yield break;

            foreach (var dc in snapshot.DomainControllers)
            {
                if (dc == null || string.IsNullOrEmpty(dc.HostName)) continue;

                var level = OsObsolescenceClassifier.Classify(dc.OSVersion);
                var issue = BuildIssue(dc, level);
                if (issue != null) yield return issue;
            }
        }

        private static DetectedIssue BuildIssue(DomainControllerInfo dc, OsObsolescenceLevel level)
        {
            switch (level)
            {
                case OsObsolescenceLevel.UnsupportedCritical:
                    return new DetectedIssue
                    {
                        Code        = "OS_UNSUPPORTED",
                        Severity    = IssueSeverity.Critical,
                        Title       = $"OS non supporté sur {dc.HostName} ({dc.OSVersion})",
                        Description =
                            "Cette version de Windows Server ne reçoit plus aucun correctif " +
                            "de sécurité, même sous ESU. Planifiez une migration immédiate " +
                            "vers Windows Server 2019, 2022 ou 2025.",
                        AffectedItems = new[] { dc.HostName }
                    };

                case OsObsolescenceLevel.OutOfExtendedSupport:
                    return new DetectedIssue
                    {
                        Code        = "OS_OUT_OF_EXTENDED_SUPPORT",
                        Severity    = IssueSeverity.Warning,
                        Title       = $"OS hors support étendu sur {dc.HostName} ({dc.OSVersion})",
                        Description =
                            "Windows Server 2012 / 2012 R2 est hors support étendu depuis " +
                            "octobre 2023. Les correctifs ne sont disponibles que via un " +
                            "abonnement ESU. Planifiez une migration vers Server 2019 ou plus récent.",
                        AffectedItems = new[] { dc.HostName }
                    };

                case OsObsolescenceLevel.Unknown:
                    // OSVersion vide ou format non reconnu : on l'indique sans bruit.
                    return new DetectedIssue
                    {
                        Code        = "OS_UNKNOWN",
                        Severity    = IssueSeverity.Info,
                        Title       = $"Version d'OS inconnue sur {dc.HostName}",
                        Description =
                            "La propriété OSVersion est vide ou n'a pas pu être interprétée. " +
                            "Vérifiez manuellement la version de Windows Server installée.",
                        AffectedItems = new[] { dc.HostName }
                    };

                default:
                    return null;
            }
        }
    }
}
