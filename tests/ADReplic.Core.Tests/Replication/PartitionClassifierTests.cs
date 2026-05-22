using ADReplic.Core.Replication;
using Xunit;

namespace ADReplic.Core.Tests.Replication
{
    public class PartitionClassifierTests
    {
        [Fact]
        public void Schema_partition_is_recognized()
        {
            var dn = "CN=Schema,CN=Configuration,DC=scopi,DC=local";
            Assert.Equal(PartitionClassifier.SchemaLabel, PartitionClassifier.Classify(dn));
        }

        [Fact]
        public void Configuration_partition_is_recognized()
        {
            var dn = "CN=Configuration,DC=scopi,DC=local";
            Assert.Equal(PartitionClassifier.ConfigurationLabel, PartitionClassifier.Classify(dn));
        }

        [Fact]
        public void Schema_takes_precedence_over_Configuration()
        {
            // Schema commence par "CN=Schema,CN=Configuration" donc l'ordre des règles est critique.
            var dn = "CN=Schema,CN=Configuration,DC=scopi,DC=local";
            Assert.NotEqual(PartitionClassifier.ConfigurationLabel, PartitionClassifier.Classify(dn));
        }

        [Theory]
        [InlineData("DC=DomainDnsZones,DC=scopi,DC=local")]
        [InlineData("DC=ForestDnsZones,DC=scopi,DC=local")]
        public void DnsZones_partitions_are_recognized(string dn)
        {
            Assert.Equal(PartitionClassifier.DnsAppLabel, PartitionClassifier.Classify(dn));
        }

        [Fact]
        public void Standard_domain_partition_is_recognized()
        {
            var dn = "DC=scopi,DC=local";
            Assert.Equal(PartitionClassifier.DomainLabel, PartitionClassifier.Classify(dn));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Null_or_whitespace_returns_unknown(string dn)
        {
            Assert.Equal(PartitionClassifier.UnknownLabel, PartitionClassifier.Classify(dn));
        }

        [Fact]
        public void Classification_is_case_insensitive()
        {
            var dn = "cn=schema,cn=configuration,dc=scopi,dc=local";
            Assert.Equal(PartitionClassifier.SchemaLabel, PartitionClassifier.Classify(dn));
        }
    }
}
