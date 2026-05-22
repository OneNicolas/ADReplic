using System.Collections.Generic;

namespace ADReplic.Core.Health.Ports
{
    /// <summary>
    /// Liste canonique des ports TCP testés par PortHealthProbe.
    /// Le pattern miroir d'IssueAggregator : une liste par défaut exposée
    /// statiquement, surchargeable par injection pour les tests ou pour
    /// adapter le périmètre à des contextes particuliers.
    ///
    /// Les ports retenus couvrent les services AD critiques côté DC entrant :
    /// résolution (DNS), authentification (Kerberos, kpasswd), annuaire (LDAP/LDAPS),
    /// catalogue global (GC), partage (SMB) et RPC. Aucun port sortant ni dynamique.
    /// </summary>
    public static class AdServicePorts
    {
        public static IReadOnlyList<AdServicePort> Default { get; } = new[]
        {
            new AdServicePort(53,   "DNS"),
            new AdServicePort(88,   "Kerberos"),
            new AdServicePort(135,  "RPC Endpoint Mapper"),
            new AdServicePort(389,  "LDAP"),
            new AdServicePort(445,  "SMB"),
            new AdServicePort(464,  "Kerberos Password"),
            new AdServicePort(636,  "LDAPS"),
            new AdServicePort(3268, "Global Catalog"),
            new AdServicePort(3269, "Global Catalog LDAPS"),
        };
    }

    /// <summary>Couple (port TCP, libellé du service exposé).</summary>
    public sealed class AdServicePort
    {
        public int Port { get; }
        public string ServiceLabel { get; }

        public AdServicePort(int port, string serviceLabel)
        {
            Port = port;
            ServiceLabel = serviceLabel;
        }
    }
}
