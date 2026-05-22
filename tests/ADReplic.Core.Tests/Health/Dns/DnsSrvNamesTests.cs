using System;
using System.Linq;
using ADReplic.Core.Health.Dns;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Health.Dns
{
    public class DnsSrvNamesTests
    {
        [Fact]
        public void Builds_four_srv_queries()
        {
            var queries = DnsSrvNames.Build("exemple.local", "exemple.local");

            Assert.Equal(4, queries.Count);
        }

        [Fact]
        public void Builds_ldap_srv_under_msdcs_subtree_of_forest()
        {
            var queries = DnsSrvNames.Build("exemple.local", "filiale.exemple.local");

            var ldap = queries.Single(q => q.Category == DnsCheckedRecordType.SrvLdap);
            Assert.Equal("_ldap._tcp.dc._msdcs.exemple.local", ldap.Name);
        }

        [Fact]
        public void Builds_gc_srv_under_forest_root()
        {
            var queries = DnsSrvNames.Build("exemple.local", "filiale.exemple.local");

            var gc = queries.Single(q => q.Category == DnsCheckedRecordType.SrvGc);
            Assert.Equal("_gc._tcp.exemple.local", gc.Name);
        }

        [Fact]
        public void Builds_kerberos_srv_under_domain_not_forest()
        {
            var queries = DnsSrvNames.Build("exemple.local", "filiale.exemple.local");

            var kerberos = queries.Single(q => q.Category == DnsCheckedRecordType.SrvKerberos);
            Assert.Equal("_kerberos._tcp.filiale.exemple.local", kerberos.Name);
        }

        [Fact]
        public void Builds_kpasswd_srv_under_domain_not_forest()
        {
            var queries = DnsSrvNames.Build("exemple.local", "filiale.exemple.local");

            var kpasswd = queries.Single(q => q.Category == DnsCheckedRecordType.SrvKpasswd);
            Assert.Equal("_kpasswd._tcp.filiale.exemple.local", kpasswd.Name);
        }

        [Fact]
        public void Trims_trailing_dot_in_names()
        {
            var queries = DnsSrvNames.Build("exemple.local.", "exemple.local.");

            Assert.All(queries, q => Assert.DoesNotContain("..", q.Name));
            Assert.All(queries, q => Assert.False(q.Name.EndsWith(".")));
        }

        [Fact]
        public void Throws_when_forest_is_null_or_blank()
        {
            Assert.Throws<ArgumentException>(() => DnsSrvNames.Build(null, "exemple.local"));
            Assert.Throws<ArgumentException>(() => DnsSrvNames.Build("", "exemple.local"));
            Assert.Throws<ArgumentException>(() => DnsSrvNames.Build("   ", "exemple.local"));
        }

        [Fact]
        public void Throws_when_domain_is_null_or_blank()
        {
            Assert.Throws<ArgumentException>(() => DnsSrvNames.Build("exemple.local", null));
            Assert.Throws<ArgumentException>(() => DnsSrvNames.Build("exemple.local", ""));
            Assert.Throws<ArgumentException>(() => DnsSrvNames.Build("exemple.local", "   "));
        }
    }
}
