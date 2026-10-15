using System;
using System.Linq;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Diagnostics.Issues
{
    /// <summary>
    /// DC_CLOCK_SKEW (v0.8.0) : un DC dont l'horloge s'écarte de plus de 5 minutes
    /// de la référence (le maître PDC s'il est mesuré, sinon la médiane) casse
    /// Kerberos. Les décalages sont comparés entre DC : l'horloge du poste d'audit
    /// s'annule dans la différence.
    /// </summary>
    public class ClockSkewDetectorTests
    {
        private static ClockCheckResult Check(string host, double offsetMinutes, ClockCheckStatus status = ClockCheckStatus.Ok)
            => new ClockCheckResult
            {
                HostName = host,
                Status = status,
                OffsetFromAuditHost = status == ClockCheckStatus.Ok ? TimeSpan.FromMinutes(offsetMinutes) : (TimeSpan?)null
            };

        private static AuditSnapshot Snapshot(string pdc, params ClockCheckResult[] checks)
            => new AuditSnapshot
            {
                DomainControllers = checks.Select(c => new DomainControllerInfo
                {
                    HostName = c.HostName,
                    Roles = string.Equals(c.HostName, pdc, StringComparison.OrdinalIgnoreCase) ? new[] { "PDC" } : new string[0]
                }).ToList(),
                ClockHealth = new ClockHealthResult { Checks = checks }
            };

        [Fact]
        public void Silent_without_clock_probe()
        {
            Assert.Empty(new ClockSkewDetector().Detect(new AuditSnapshot { DomainControllers = new DomainControllerInfo[0] }));
        }

        [Fact]
        public void Silent_when_every_dc_is_within_threshold()
        {
            // Le poste d'audit a lui-même 10 min d'avance : tous les DC paraissent en retard
            // de 10 min, mais entre eux ils sont alignés. Aucune anomalie.
            var snap = Snapshot("dc01", Check("dc01", -10), Check("dc02", -10.5), Check("dc03", -9.2));

            Assert.Empty(new ClockSkewDetector().Detect(snap));
        }

        [Fact]
        public void Reports_each_dc_drifting_from_the_pdc()
        {
            var snap = Snapshot("dc01", Check("dc01", 0), Check("dc02", 7), Check("dc03", -6), Check("dc04", 1));

            var issues = new ClockSkewDetector().Detect(snap).ToList();

            Assert.Equal(2, issues.Count);
            var dc02 = issues.Single(i => i.RelatedHost == "dc02");
            Assert.Equal("DC_CLOCK_SKEW", dc02.Code);
            Assert.Equal(IssueSeverity.Critical, dc02.Severity);
            Assert.Contains("+7 min", dc02.Title);
            Assert.Contains("dc01", dc02.Title);
            Assert.Contains("maître PDC", dc02.Title);
            Assert.Equal(new[] { "dc02", "dc01" }, dc02.AffectedItems);
            Assert.Contains("-6 min", issues.Single(i => i.RelatedHost == "dc03").Title);
        }

        [Fact]
        public void Falls_back_to_the_median_when_the_pdc_clock_was_not_measured()
        {
            // PDC injoignable : la médiane (dc03 = 0) sert de référence, dc04 est le seul hors seuil.
            var snap = Snapshot("dc01",
                Check("dc01", 0, ClockCheckStatus.Skipped), Check("dc02", -1), Check("dc03", 0), Check("dc04", 8));

            var issue = Assert.Single(new ClockSkewDetector().Detect(snap));

            Assert.Equal("dc04", issue.RelatedHost);
            Assert.DoesNotContain("maître PDC", issue.Title);
        }

        [Fact]
        public void The_pdc_itself_is_the_one_reported_when_it_is_the_odd_one_out()
        {
            // Trois DC alignés, le PDC seul décalé : la référence reste le PDC (source
            // de temps autoritaire d'AD), donc les trois autres sont signalés. C'est
            // volontaire : c'est bien le PDC qu'il faut regarder en premier, et le
            // constat le dit.
            var snap = Snapshot("dc01", Check("dc01", 9), Check("dc02", 0), Check("dc03", 0.5));

            var issues = new ClockSkewDetector().Detect(snap).ToList();

            Assert.Equal(2, issues.Count);
            Assert.All(issues, i => Assert.Contains("dc01", i.AffectedItems));
        }

        [Fact]
        public void Needs_at_least_two_measured_dcs()
        {
            var snap = Snapshot("dc01", Check("dc01", 0), Check("dc02", 30, ClockCheckStatus.Error));

            Assert.Empty(new ClockSkewDetector().Detect(snap));
        }
    }
}
