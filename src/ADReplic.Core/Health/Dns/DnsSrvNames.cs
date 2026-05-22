using System;
using System.Collections.Generic;
using ADReplic.Core.Models;

namespace ADReplic.Core.Health.Dns
{
    /// <summary>
    /// Compose les noms DNS SRV à vérifier pour une forêt AD.
    /// Logique pure (string assembly), aucune dépendance externe : testable isolément.
    ///
    /// Les 4 SRV vérifiés alignent ce qu'AD considère comme indispensable au
    /// fonctionnement de la forêt :
    /// - _ldap._tcp.dc._msdcs.&lt;forêt&gt;     -> localisation des DC de la forêt
    /// - _gc._tcp.&lt;forêt&gt;                  -> localisation des Global Catalogs
    /// - _kerberos._tcp.&lt;domaine&gt;          -> KDC du domaine
    /// - _kpasswd._tcp.&lt;domaine&gt;           -> service de changement de mot de passe
    /// </summary>
    public static class DnsSrvNames
    {
        public static IReadOnlyList<DnsSrvQuery> Build(string forestDnsName, string domainDnsName)
        {
            if (string.IsNullOrWhiteSpace(forestDnsName))
                throw new ArgumentException("Nom de forêt requis", nameof(forestDnsName));
            if (string.IsNullOrWhiteSpace(domainDnsName))
                throw new ArgumentException("Nom de domaine requis", nameof(domainDnsName));

            var forest = forestDnsName.Trim().TrimEnd('.');
            var domain = domainDnsName.Trim().TrimEnd('.');

            return new[]
            {
                new DnsSrvQuery("_ldap._tcp.dc._msdcs." + forest,  DnsCheckedRecordType.SrvLdap),
                new DnsSrvQuery("_gc._tcp." + forest,              DnsCheckedRecordType.SrvGc),
                new DnsSrvQuery("_kerberos._tcp." + domain,        DnsCheckedRecordType.SrvKerberos),
                new DnsSrvQuery("_kpasswd._tcp." + domain,         DnsCheckedRecordType.SrvKpasswd),
            };
        }
    }

    /// <summary>Couple (nom DNS, catégorie fonctionnelle) à interroger.</summary>
    public sealed class DnsSrvQuery
    {
        public string Name { get; }
        public DnsCheckedRecordType Category { get; }

        public DnsSrvQuery(string name, DnsCheckedRecordType category)
        {
            Name = name;
            Category = category;
        }
    }
}
