using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Représente un site Active Directory et ses caractéristiques de topologie.
    /// </summary>
    public sealed class SiteInfo
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> DomainControllers { get; set; }
        public IReadOnlyList<string> Subnets { get; set; }
        public IReadOnlyList<string> BridgeheadServers { get; set; }
    }
}
