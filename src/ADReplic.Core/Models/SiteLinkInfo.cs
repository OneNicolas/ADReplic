using System;
using System.Collections.Generic;

namespace ADReplic.Core.Models
{
    /// <summary>
    /// Représente un lien de site (Site Link) : qui parle à qui, à quel coût et à quelle fréquence.
    /// </summary>
    public sealed class SiteLinkInfo
    {
        public string Name { get; set; }
        public int Cost { get; set; }
        public TimeSpan ReplicationInterval { get; set; }
        public string TransportType { get; set; }
        public bool ChangeNotificationEnabled { get; set; }
        public IReadOnlyList<string> Sites { get; set; }
    }
}
