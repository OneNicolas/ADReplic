using System;
using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.Core.Comparison
{
    /// <summary>
    /// Extrait d'un <see cref="AuditDiff"/> le sous-ensemble de changements
    /// suffisamment importants pour déclencher une alerte en mode auto-refresh.
    /// L'intégralité des différences reste accessible via <see cref="AuditDiff"/> ;
    /// ce détecteur applique des heuristiques métier précises pour limiter le bruit.
    /// </summary>
    public static class SignificantChangeDetector
    {
        /// <summary>Ports TCP dont la fermeture est considérée comme critique.</summary>
        private static readonly HashSet<int> CriticalPorts = new HashSet<int> { 389, 88, 445 };

        /// <summary>Types de SRV records dont l'absence rend l'AD globalement inutilisable.</summary>
        private static readonly HashSet<DnsCheckedRecordType> CriticalSrvTypes =
            new HashSet<DnsCheckedRecordType>
            {
                DnsCheckedRecordType.SrvLdap,
                DnsCheckedRecordType.SrvKerberos
            };

        public static IReadOnlyList<SignificantChange> Detect(AuditDiff diff)
        {
            if (diff == null) throw new ArgumentNullException(nameof(diff));

            var changes = new List<SignificantChange>();

            DetectLevelDegradation(diff, changes);
            DetectNewCriticalIssues(diff, changes);
            DetectLostDcs(diff, changes);
            DetectNewFailures(diff, changes);
            DetectLostCriticalSrv(diff, changes);
            DetectClosedCriticalPorts(diff, changes);

            return OrderBySeverity(changes);
        }

        // === Critère 1 : transition descendante du palier de score ===

        private static void DetectLevelDegradation(AuditDiff diff, List<SignificantChange> changes)
        {
            if (diff.Score == null) return;
            if (!diff.Score.LevelDegraded) return;

            var severity = diff.Score.LevelAfter == HealthLevel.Critical
                ? IssueSeverity.Critical
                : IssueSeverity.Warning;

            changes.Add(new SignificantChange
            {
                Code = "SC_LEVEL_DOWN",
                Severity = severity,
                Title = $"Niveau de sante degrade : {diff.Score.LevelBefore} -> {diff.Score.LevelAfter}",
                Description =
                    $"Le score global est passe de {diff.Score.ValueBefore} a {diff.Score.ValueAfter} " +
                    $"(delta {diff.Score.Delta:+0;-0;0})."
            });
        }

        // === Critère 2 : nouvelle issue de sévérité Critical ===

        private static void DetectNewCriticalIssues(AuditDiff diff, List<SignificantChange> changes)
        {
            foreach (var issue in diff.Issues.Added.Where(i => i.Severity == IssueSeverity.Critical))
            {
                changes.Add(new SignificantChange
                {
                    Code = "SC_NEW_CRITICAL_ISSUE",
                    Severity = IssueSeverity.Critical,
                    Title = $"Nouvelle anomalie critique : {issue.Code}",
                    Description = issue.Title ?? issue.Description ?? string.Empty,
                    AffectedItems = issue.AffectedItems ?? new List<string>()
                });
            }
        }

        // === Critère 3 : DC qui disparaît de l'inventaire ===

        private static void DetectLostDcs(AuditDiff diff, List<SignificantChange> changes)
        {
            foreach (var dc in diff.DomainControllers.Removed)
            {
                changes.Add(new SignificantChange
                {
                    Code = "SC_DC_LOST",
                    Severity = IssueSeverity.Critical,
                    Title = $"Controleur de domaine disparu de l'inventaire : {dc.HostName}",
                    Description =
                        "Le DC etait present dans l'audit precedent et n'a pas ete decouvert " +
                        "dans la passe courante. Verifier connectivite, etat AD et droits.",
                    AffectedItems = new[] { dc.HostName ?? "(inconnu)" }
                });
            }
        }

        // === Critère 4 : nouveau failure de réplication actif ===

        private static void DetectNewFailures(AuditDiff diff, List<SignificantChange> changes)
        {
            foreach (var failure in diff.ReplicationFailures.Added)
            {
                changes.Add(new SignificantChange
                {
                    Code = "SC_NEW_FAILURE",
                    Severity = IssueSeverity.Warning,
                    Title =
                        $"Nouvel echec de replication : {failure.SourceDc} -> {failure.DestinationDc}",
                    Description =
                        $"Code d'erreur {failure.LastErrorCode} - {failure.LastErrorMessage ?? "sans message"}.",
                    AffectedItems = new[]
                    {
                        $"{failure.SourceDc} -> {failure.DestinationDc}"
                    }
                });
            }
        }

        // === Critère 5 : SRV critique (_ldap, _kerberos) qui devient indisponible ===

        private static void DetectLostCriticalSrv(AuditDiff diff, List<SignificantChange> changes)
        {
            // Cas A : le record SRV critique a tout simplement disparu de la liste des checks.
            foreach (var srv in diff.DnsChecks.Removed.Where(c => CriticalSrvTypes.Contains(c.Type)))
            {
                changes.Add(new SignificantChange
                {
                    Code = "SC_SRV_CRITICAL_LOST",
                    Severity = IssueSeverity.Critical,
                    Title = $"SRV critique disparu : {srv.RecordName}",
                    Description =
                        "Ce record SRV etait verifie dans l'audit precedent mais n'apparait plus " +
                        "dans la passe courante. La perte d'un SRV LDAP ou Kerberos rend l'AD inoperant.",
                    AffectedItems = new[] { srv.RecordName ?? "(inconnu)" }
                });
            }

            // Cas B : le record est toujours là mais sa résolution est passée de Ok à Missing/Error.
            foreach (var change in diff.DnsChecks.Changed
                         .Where(c => CriticalSrvTypes.Contains(c.After.Type))
                         .Where(c => c.Before.Status == DnsCheckStatus.Ok
                                  && c.After.Status != DnsCheckStatus.Ok))
            {
                changes.Add(new SignificantChange
                {
                    Code = "SC_SRV_CRITICAL_LOST",
                    Severity = IssueSeverity.Critical,
                    Title = $"SRV critique non resolu : {change.After.RecordName}",
                    Description =
                        $"Le record etait OK et est desormais en etat {change.After.Status}. " +
                        "Verifier la sante du service DNS du domaine.",
                    AffectedItems = new[] { change.After.RecordName ?? "(inconnu)" }
                });
            }
        }

        // === Critère 6 : port AD critique (389/88/445) qui passe à fermé/timeout ===

        private static void DetectClosedCriticalPorts(AuditDiff diff, List<SignificantChange> changes)
        {
            foreach (var change in diff.PortChecks.Changed
                         .Where(c => CriticalPorts.Contains(c.After.Port))
                         .Where(c => c.Before.Status == PortCheckStatus.Open
                                  && (c.After.Status == PortCheckStatus.Closed
                                      || c.After.Status == PortCheckStatus.Timeout)))
            {
                changes.Add(new SignificantChange
                {
                    Code = "SC_PORT_CRITICAL_DOWN",
                    Severity = IssueSeverity.Critical,
                    Title =
                        $"Port critique inaccessible : {change.After.HostName}:{change.After.Port} " +
                        $"({change.After.ServiceLabel})",
                    Description =
                        $"Le port etait ouvert dans l'audit precedent et est desormais en etat " +
                        $"{change.After.Status}. Service indisponible ou pare-feu bloquant.",
                    AffectedItems = new[]
                    {
                        $"{change.After.HostName}:{change.After.Port}"
                    }
                });
            }
        }

        // === Tri final : critiques en tête, puis par code stable ===

        private static IReadOnlyList<SignificantChange> OrderBySeverity(List<SignificantChange> changes) =>
            changes
                .OrderByDescending(c => c.Severity)
                .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
