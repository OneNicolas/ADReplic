using System;
using ADReplic.Core.Diagnostics;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics
{
    public class DurationFormatterTests
    {
        [Theory]
        [InlineData(0, 0, 0, 30, "moins d'une minute")]
        [InlineData(0, 0, 1, 0, "1 min")]
        [InlineData(0, 0, 45, 0, "45 min")]
        [InlineData(0, 1, 0, 0, "1 h")]
        [InlineData(0, 3, 15, 0, "3 h 15 min")]
        [InlineData(1, 0, 0, 0, "1 j")]
        [InlineData(3, 4, 0, 0, "3 j 4 h")]
        [InlineData(12, 0, 30, 0, "12 j")]
        [InlineData(45, 6, 0, 0, "45 j")]
        public void ToHumanText_formats_from_largest_unit(int d, int h, int m, int s, string expected)
        {
            var span = new TimeSpan(d, h, m, s);

            Assert.Equal(expected, DurationFormatter.ToHumanText(span));
        }

        [Fact]
        public void ToHumanText_handles_negative_as_zero()
        {
            Assert.Equal("moins d'une minute", DurationFormatter.ToHumanText(TimeSpan.FromMinutes(-5)));
        }

        [Fact]
        public void ToHumanText_handles_null()
        {
            Assert.Equal("", DurationFormatter.ToHumanText(null));
        }
    }
}
