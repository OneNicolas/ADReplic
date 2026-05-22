using System.Collections.Generic;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using Xunit;

namespace ADReplic.Core.Tests.Export
{
    public class AuditSnapshotBuilderTests
    {
        [Fact]
        public void Counts_links_by_status()
        {
            var links = new[]
            {
                new ReplicationLink { Status = ReplicationLinkStatus.Healthy },
                new ReplicationLink { Status = ReplicationLinkStatus.Healthy },
                new ReplicationLink { Status = ReplicationLinkStatus.Warning },
                new ReplicationLink { Status = ReplicationLinkStatus.Failing },
                new ReplicationLink { Status = ReplicationLinkStatus.Unreachable },
            };

            var snapshot = AuditSnapshotBuilder.Build("scopi.local", new DomainControllerInfo[0], links);

            Assert.Equal(2, snapshot.Summary.HealthyLinks);
            Assert.Equal(1, snapshot.Summary.WarningLinks);
            Assert.Equal(1, snapshot.Summary.FailingLinks);
            Assert.Equal(1, snapshot.Summary.UnreachableDcs);
            Assert.Equal(5, snapshot.Summary.ReplicationLinkCount);
        }

        [Fact]
        public void Sets_forest_name_and_metadata()
        {
            var snapshot = AuditSnapshotBuilder.Build("scopi.local", null, null);

            Assert.Equal("scopi.local", snapshot.ForestName);
            Assert.False(string.IsNullOrEmpty(snapshot.GeneratedBy));
            Assert.False(string.IsNullOrEmpty(snapshot.GeneratedOn));
            Assert.NotEqual(default, snapshot.GeneratedAt);
        }

        [Fact]
        public void Null_collections_are_replaced_by_empty_arrays()
        {
            var snapshot = AuditSnapshotBuilder.Build("scopi.local", null, null);

            Assert.NotNull(snapshot.DomainControllers);
            Assert.NotNull(snapshot.ReplicationLinks);
            Assert.NotNull(snapshot.ReplicationFailures);
            Assert.NotNull(snapshot.Sites);
            Assert.NotNull(snapshot.SiteLinks);
            Assert.Empty(snapshot.DomainControllers);
            Assert.Empty(snapshot.ReplicationLinks);
        }

        [Fact]
        public void DC_count_in_summary_matches_input()
        {
            var dcs = new[]
            {
                new DomainControllerInfo { HostName = "DC01" },
                new DomainControllerInfo { HostName = "DC02" },
                new DomainControllerInfo { HostName = "DC03" },
            };

            var snapshot = AuditSnapshotBuilder.Build("scopi.local", dcs, null);

            Assert.Equal(3, snapshot.Summary.DomainControllerCount);
        }

        [Fact]
        public void Is_single_dc_mode_defaults_to_false()
        {
            var snapshot = AuditSnapshotBuilder.Build("scopi.local", null, null);
            Assert.False(snapshot.IsSingleDcMode);
        }

        [Fact]
        public void Is_single_dc_mode_is_propagated_to_snapshot()
        {
            var snapshot = AuditSnapshotBuilder.Build(
                "scopi.local", null, null, null, null, isSingleDcMode: true);
            Assert.True(snapshot.IsSingleDcMode);
        }
    }
}
