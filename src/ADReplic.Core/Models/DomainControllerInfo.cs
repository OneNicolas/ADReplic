using System;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Snapshot d'un contrôleur de domaine retourné par la phase de découverte.
    /// </summary>
    public sealed class DomainControllerInfo
    {
        public string HostName { get; set; }
        public string Domain { get; set; }
        public string Forest { get; set; }
        public string SiteName { get; set; }
        public string IPAddress { get; set; }
        public string OSVersion { get; set; }
        public bool IsGlobalCatalog { get; set; }
        public bool IsReadOnly { get; set; }
        public string[] Roles { get; set; }
        public DateTime DiscoveredAt { get; set; }

        public override string ToString() => HostName ?? "(unknown DC)";
    }
}
