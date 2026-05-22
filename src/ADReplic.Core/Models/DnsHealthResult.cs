using System;
using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Agrégat des vérifications DNS exécutées lors d'un audit.
    /// Sérialisé tel quel dans le JSON exporter (via réflexion) et exposé dans
    /// AuditSnapshot pour alimenter la GUI, les CSV et le scoring tripartite.
    /// </summary>
    public sealed class DnsHealthResult
    {
        public IReadOnlyList<DnsCheckResult> Checks { get; set; }
        public DateTime CapturedAtUtc { get; set; }

        public DnsHealthResult()
        {
            Checks = new List<DnsCheckResult>();
            CapturedAtUtc = DateTime.UtcNow;
        }
    }
}
