using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics.Issues
{
    /// <summary>
    /// Détecteur d'une catégorie d'anomalie. Contrat stateless et idempotent :
    /// reçoit un snapshot, retourne 0..N issues, ne mute rien.
    /// </summary>
    public interface IIssueDetector
    {
        IEnumerable<DetectedIssue> Detect(AuditSnapshot snapshot);
    }
}
