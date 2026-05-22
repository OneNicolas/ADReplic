using System;
using ADReplic.Core.Diagnostics;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    public class FailureSeverityClassifierTests
    {
        [Fact]
        public void Recent_when_less_than_one_hour()
        {
            var severity = FailureSeverityClassifier.Classify(TimeSpan.FromMinutes(30));
            Assert.Equal(ReplicationFailureSeverity.Recent, severity);
        }

        [Fact]
        public void Sustained_when_between_1_and_24_hours()
        {
            var severity = FailureSeverityClassifier.Classify(TimeSpan.FromHours(5));
            Assert.Equal(ReplicationFailureSeverity.Sustained, severity);
        }

        [Fact]
        public void Critical_when_above_24_hours()
        {
            var severity = FailureSeverityClassifier.Classify(TimeSpan.FromHours(25));
            Assert.Equal(ReplicationFailureSeverity.Critical, severity);
        }

        [Theory]
        [InlineData(0, ReplicationFailureSeverity.Recent)]
        [InlineData(59, ReplicationFailureSeverity.Recent)]
        [InlineData(60, ReplicationFailureSeverity.Sustained)]
        [InlineData(1439, ReplicationFailureSeverity.Sustained)]
        [InlineData(1440, ReplicationFailureSeverity.Critical)]
        public void Boundary_values(int minutes, ReplicationFailureSeverity expected)
        {
            var severity = FailureSeverityClassifier.Classify(TimeSpan.FromMinutes(minutes));
            Assert.Equal(expected, severity);
        }
    }
}
