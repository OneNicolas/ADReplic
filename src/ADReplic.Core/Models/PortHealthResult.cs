using System;
using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Agrégat des tests de ports exécutés lors d'un audit.
    /// Sérialisé tel quel dans le JSON exporter (via réflexion) et exposé dans
    /// AuditSnapshot pour alimenter la GUI, les CSV et le scoring tripartite.
    /// </summary>
    public sealed class PortHealthResult
    {
        public IReadOnlyList<PortCheckResult> Checks { get; set; }
        public DateTime CapturedAtUtc { get; set; }

        public PortHealthResult()
        {
            Checks = new List<PortCheckResult>();
            CapturedAtUtc = DateTime.UtcNow;
        }
    }
}
