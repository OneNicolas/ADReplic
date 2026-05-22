using ADReplic.Core.Diagnostics.Issues;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    public class OsObsolescenceClassifierTests
    {
        [Theory]
        [InlineData("Windows Server 2025 Datacenter")]
        [InlineData("Windows Server 2022 Standard")]
        [InlineData("Windows Server 2019 Datacenter")]
        [InlineData("Windows Server 2016 Standard")]
        public void Recent_versions_are_supported(string os)
        {
            Assert.Equal(OsObsolescenceLevel.None, OsObsolescenceClassifier.Classify(os));
        }

        [Theory]
        [InlineData("Windows Server 2012 R2 Standard")]
        [InlineData("Windows Server 2012 Datacenter")]
        public void Server_2012_is_out_of_extended_support(string os)
        {
            Assert.Equal(OsObsolescenceLevel.OutOfExtendedSupport, OsObsolescenceClassifier.Classify(os));
        }

        [Theory]
        [InlineData("Windows Server 2008 R2 Enterprise")]
        [InlineData("Windows Server 2008 Standard")]
        [InlineData("Windows Server 2003 R2 Standard")]
        [InlineData("Windows Server 2003 Enterprise Edition")]
        public void Old_versions_are_critical(string os)
        {
            Assert.Equal(OsObsolescenceLevel.UnsupportedCritical, OsObsolescenceClassifier.Classify(os));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Linux Debian 12")]
        [InlineData("FreeBSD 14")]
        public void Empty_or_unknown_strings_are_classified_as_unknown(string os)
        {
            Assert.Equal(OsObsolescenceLevel.Unknown, OsObsolescenceClassifier.Classify(os));
        }

        [Theory]
        [InlineData("windows server 2022 standard")]
        [InlineData("WINDOWS SERVER 2016 DATACENTER")]
        public void Matching_is_case_insensitive(string os)
        {
            Assert.Equal(OsObsolescenceLevel.None, OsObsolescenceClassifier.Classify(os));
        }
    }
}
