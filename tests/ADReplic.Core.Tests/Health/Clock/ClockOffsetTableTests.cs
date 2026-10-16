using System;
using System.Linq;
using ADReplic.Core.Health.Clock;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Health.Clock
{
    /// <summary>
    /// La table des décalages est la source unique de « par rapport à quoi » :
    /// même référence (PDC mesuré, sinon médiane) pour le détecteur DC_CLOCK_SKEW,
    /// la colonne Décalage de la GUI et la section Horloges du HTML.
    /// </summary>
    public class ClockOffsetTableTests
    {
        private static ClockCheckResult Check(string host, double offsetMinutes, ClockCheckStatus status = ClockCheckStatus.Ok)
            => new ClockCheckResult
            {
                HostName = host,
                Status = status,
                OffsetFromAuditHost = status == ClockCheckStatus.Ok ? TimeSpan.FromMinutes(offsetMinutes) : (TimeSpan?)null,
                Detail = status == ClockCheckStatus.Ok ? null : status.ToString()
            };

        private static DomainControllerInfo Dc(string host, bool pdc = false)
            => new DomainControllerInfo { HostName = host, Roles = pdc ? new[] { "PDC" } : new string[0] };

        [Fact]
        public void Without_probe_the_table_has_no_data()
        {
            var table = ClockOffsetTable.Build(new[] { Dc("dc01") }, null);

            Assert.False(table.HasData);
            Assert.Empty(table.Entries);
            Assert.Null(table.ReferenceHost);
            Assert.Null(table.Find("dc01"));
        }

        [Fact]
        public void Pdc_is_the_reference_when_measured()
        {
            var table = ClockOffsetTable.Build(
                new[] { Dc("dc01"), Dc("dc02", pdc: true) },
                new ClockHealthResult { Checks = new[] { Check("dc01", 3), Check("dc02", 1) } });

            Assert.Equal("dc02", table.ReferenceHost);
            Assert.True(table.ReferenceIsPdc);
            Assert.True(table.Find("dc02").IsReference);
            Assert.Null(table.Find("dc02").OffsetFromReference);
            Assert.Equal(TimeSpan.FromMinutes(2), table.Find("dc01").OffsetFromReference);
        }

        [Fact]
        public void Median_is_the_reference_when_pdc_is_not_measured()
        {
            var table = ClockOffsetTable.Build(
                new[] { Dc("dc01", pdc: true), Dc("dc02"), Dc("dc03"), Dc("dc04") },
                new ClockHealthResult
                {
                    Checks = new[] { Check("dc01", 0, ClockCheckStatus.Timeout), Check("dc02", -10), Check("dc03", 0), Check("dc04", 5) }
                });

            Assert.Equal("dc03", table.ReferenceHost);
            Assert.False(table.ReferenceIsPdc);
            Assert.Equal(TimeSpan.FromMinutes(-10), table.Find("dc02").OffsetFromReference);
            Assert.Equal(TimeSpan.FromMinutes(5), table.Find("dc04").OffsetFromReference);
        }

        [Fact]
        public void Unmeasured_dc_keeps_its_status_and_has_no_offset()
        {
            var table = ClockOffsetTable.Build(
                new[] { Dc("dc01", pdc: true), Dc("dc02") },
                new ClockHealthResult { Checks = new[] { Check("dc01", 0), Check("dc02", 0, ClockCheckStatus.Skipped) } });

            var entry = table.Find("dc02");

            Assert.Equal(ClockCheckStatus.Skipped, entry.Status);
            Assert.Null(entry.OffsetFromReference);
            Assert.False(entry.IsReference);
        }

        [Fact]
        public void Single_measured_dc_is_its_own_reference()
        {
            var table = ClockOffsetTable.Build(
                new[] { Dc("dc01") },
                new ClockHealthResult { Checks = new[] { Check("dc01", 4) } });

            Assert.Equal(1, table.MeasuredCount);
            Assert.True(table.Find("dc01").IsReference);
        }

        [Fact]
        public void Find_ignores_case_and_trailing_dot()
        {
            var table = ClockOffsetTable.Build(
                new[] { Dc("dc01.exemple.local", pdc: true) },
                new ClockHealthResult { Checks = new[] { Check("dc01.exemple.local", 0) } });

            Assert.NotNull(table.Find("DC01.EXEMPLE.LOCAL."));
        }

        [Fact]
        public void Entries_follow_probe_order_and_carry_the_dc_time()
        {
            var dcTime = new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);
            var checks = new[] { Check("dc02", 1), Check("dc01", 0) };
            checks[0].DcTimeUtc = dcTime;

            var table = ClockOffsetTable.Build(new[] { Dc("dc01", pdc: true), Dc("dc02") }, new ClockHealthResult { Checks = checks });

            Assert.Equal(new[] { "dc02", "dc01" }, table.Entries.Select(e => e.HostName));
            Assert.Equal(dcTime, table.Find("dc02").DcTimeUtc);
        }
    }

    public class ClockOffsetFormatterTests
    {
        [Fact]
        public void Null_is_a_dash()
        {
            Assert.Equal("\u2014", ClockOffsetFormatter.ToText(null));
        }

        [Theory]
        [InlineData(0.4, "+0,4 s")]
        [InlineData(-0.4, "-0,4 s")]
        [InlineData(0.0, "+0,0 s")]
        [InlineData(12.26, "+12,3 s")]
        [InlineData(59.96, "+60,0 s")]
        public void Under_a_minute_shows_tenths_of_seconds(double seconds, string expected)
        {
            Assert.Equal(expected, ClockOffsetFormatter.ToText(TimeSpan.FromSeconds(seconds)));
        }

        [Theory]
        [InlineData(600, "+10 min")]
        [InlineData(-603, "-10 min 3 s")]
        [InlineData(3900, "+1 h 5 min")]
        [InlineData(-7200, "-2 h")]
        public void A_minute_or_more_shows_minutes_then_hours(double seconds, string expected)
        {
            Assert.Equal(expected, ClockOffsetFormatter.ToText(TimeSpan.FromSeconds(seconds)));
        }
    }
}
