using System;
using System.Linq;
using ADReplic.Core.Models;
using ADReplic.Core.Monitoring;
using Xunit;

namespace ADReplic.Core.Tests.Monitoring
{
    public class AuditHistoryTests
    {
        private static ScoreHistoryEntry Entry(int value, DateTime? at = null) =>
            new ScoreHistoryEntry
            {
                OverallScore = value,
                Level = HealthLevel.Excellent,
                ReplicationScore = value,
                CapturedAt = at ?? DateTime.Now
            };

        [Fact]
        public void Default_capacity_is_fifty()
        {
            var history = new AuditHistory();
            Assert.Equal(50, history.Capacity);
        }

        [Fact]
        public void Throws_on_zero_or_negative_capacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AuditHistory(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AuditHistory(-1));
        }

        [Fact]
        public void Adds_entries_in_chronological_order()
        {
            var history = new AuditHistory(3);
            history.Add(Entry(90));
            history.Add(Entry(80));
            history.Add(Entry(70));

            Assert.Equal(new[] { 90, 80, 70 }, history.Entries.Select(e => e.OverallScore));
        }

        [Fact]
        public void Evicts_oldest_when_over_capacity()
        {
            var history = new AuditHistory(2);
            history.Add(Entry(90));
            history.Add(Entry(80));
            history.Add(Entry(70));

            Assert.Equal(2, history.Count);
            Assert.Equal(new[] { 80, 70 }, history.Entries.Select(e => e.OverallScore));
        }

        [Fact]
        public void Latest_returns_last_added_entry()
        {
            var history = new AuditHistory();
            history.Add(Entry(90));
            history.Add(Entry(75));

            Assert.Equal(75, history.Latest.OverallScore);
        }

        [Fact]
        public void Latest_is_null_when_history_is_empty()
        {
            Assert.Null(new AuditHistory().Latest);
        }

        [Fact]
        public void Throws_when_adding_null_entry()
        {
            Assert.Throws<ArgumentNullException>(() => new AuditHistory().Add(null));
        }

        [Fact]
        public void Clear_removes_all_entries()
        {
            var history = new AuditHistory();
            history.Add(Entry(90));
            history.Add(Entry(80));

            history.Clear();

            Assert.Equal(0, history.Count);
            Assert.Null(history.Latest);
        }

        [Fact]
        public void Entries_is_a_defensive_copy()
        {
            var history = new AuditHistory();
            history.Add(Entry(90));

            // Si Entries était une exposition directe, la modifier casserait le buffer.
            var entries = history.Entries;
            Assert.Single(entries);

            history.Add(Entry(80));
            // L'instance retournée précédemment ne doit pas changer.
            Assert.Single(entries);
            Assert.Equal(2, history.Entries.Count);
        }

        [Fact]
        public void ScoreHistoryEntry_From_copies_relevant_fields()
        {
            var score = new HealthScore
            {
                Value = 85, Level = HealthLevel.Warning,
                ReplicationScore = 80, DnsScore = 90, PortScore = 85
            };

            var entry = ScoreHistoryEntry.From(score);

            Assert.Equal(85, entry.OverallScore);
            Assert.Equal(HealthLevel.Warning, entry.Level);
            Assert.Equal(80, entry.ReplicationScore);
            Assert.Equal(90, entry.DnsScore);
            Assert.Equal(85, entry.PortScore);
        }

        [Fact]
        public void ScoreHistoryEntry_From_throws_on_null_score()
        {
            Assert.Throws<ArgumentNullException>(() => ScoreHistoryEntry.From(null));
        }
    }
}
