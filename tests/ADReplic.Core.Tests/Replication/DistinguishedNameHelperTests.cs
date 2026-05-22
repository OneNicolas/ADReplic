using ADReplic.Core.Replication;
using Xunit;

namespace ADReplic.Core.Tests.Replication
{
    public class DistinguishedNameHelperTests
    {
        [Fact]
        public void Extracts_server_name_from_full_dn()
        {
            var dn = "CN=NTDS Settings,CN=DC01,CN=Servers,CN=Paris,CN=Sites,CN=Configuration,DC=scopi,DC=local";
            Assert.Equal("DC01", DistinguishedNameHelper.ExtractServerName(dn));
        }

        [Fact]
        public void Returns_original_when_no_ntds_settings_pattern()
        {
            var dn = "CN=Some,CN=Other,DC=scopi,DC=local";
            Assert.Equal(dn, DistinguishedNameHelper.ExtractServerName(dn));
        }

        [Fact]
        public void Returns_plain_string_unchanged()
        {
            Assert.Equal("dc01.scopi.local", DistinguishedNameHelper.ExtractServerName("dc01.scopi.local"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Null_or_empty_passes_through(string input)
        {
            Assert.Equal(input, DistinguishedNameHelper.ExtractServerName(input));
        }

        [Fact]
        public void Case_insensitive_on_marker()
        {
            var dn = "cn=ntds settings,CN=DC02,CN=Servers,CN=Lyon,CN=Sites,CN=Configuration,DC=scopi,DC=local";
            Assert.Equal("DC02", DistinguishedNameHelper.ExtractServerName(dn));
        }
    }
}
