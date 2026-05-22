using System.Collections.Generic;
using System.Linq;
using ADReplic.Core.Health.Ports;
using Xunit;

namespace ADReplic.Core.Tests.Health.Ports
{
    public class AdServicePortsTests
    {
        [Fact]
        public void Default_contains_nine_ports()
        {
            Assert.Equal(9, AdServicePorts.Default.Count);
        }

        [Fact]
        public void Default_covers_all_critical_ad_services()
        {
            var ports = new HashSet<int>(AdServicePorts.Default.Select(p => p.Port));

            // DNS, Kerberos, RPC EPM, LDAP, SMB, kpasswd, LDAPS, GC, GC LDAPS
            Assert.Contains(53, ports);
            Assert.Contains(88, ports);
            Assert.Contains(135, ports);
            Assert.Contains(389, ports);
            Assert.Contains(445, ports);
            Assert.Contains(464, ports);
            Assert.Contains(636, ports);
            Assert.Contains(3268, ports);
            Assert.Contains(3269, ports);
        }

        [Fact]
        public void Every_port_has_non_empty_service_label()
        {
            Assert.All(AdServicePorts.Default, p =>
                Assert.False(string.IsNullOrWhiteSpace(p.ServiceLabel)));
        }

        [Fact]
        public void Ldap_is_labeled_explicitly()
        {
            var ldap = AdServicePorts.Default.Single(p => p.Port == 389);
            Assert.Equal("LDAP", ldap.ServiceLabel);
        }

        [Fact]
        public void Global_catalog_ports_are_distinct_from_ldap_ports()
        {
            var gc = AdServicePorts.Default.Single(p => p.Port == 3268);
            var gcs = AdServicePorts.Default.Single(p => p.Port == 3269);
            Assert.Contains("Catalog", gc.ServiceLabel);
            Assert.Contains("LDAPS", gcs.ServiceLabel);
        }

        [Fact]
        public void Default_list_is_stable_across_calls()
        {
            var a = AdServicePorts.Default;
            var b = AdServicePorts.Default;
            Assert.Same(a, b);
        }
    }
}
