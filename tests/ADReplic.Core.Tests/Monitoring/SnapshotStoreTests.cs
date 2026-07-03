using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Export;
using ADReplic.Core.Models;
using ADReplic.Core.Monitoring;
using Xunit;

namespace ADReplic.Core.Tests.Monitoring
{
    /// <summary>
    /// Tests du SnapshotStore avec un répertoire temporaire jetable. Implémente
    /// IDisposable pour nettoyer le dossier en fin de test, évitant l'accumulation
    /// de fichiers entre runs.
    /// </summary>
    public class SnapshotStoreTests : IDisposable
    {
        private readonly string _tempDir;

        public SnapshotStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "adreplic-tests-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort */ }
        }

        private static AuditSnapshot Snapshot(DateTime? capturedAt = null, string forest = "exemple.local")
        {
            return new AuditSnapshot
            {
                ForestName = forest,
                GeneratedAt = capturedAt ?? new DateTime(2026, 5, 23, 14, 0, 0, DateTimeKind.Local),
                DomainControllers = new List<DomainControllerInfo>(),
                ReplicationLinks = new List<ReplicationLink>(),
                ReplicationFailures = new List<ReplicationFailureInfo>(),
                Sites = new List<SiteInfo>(),
                SiteLinks = new List<SiteLinkInfo>(),
                Issues = new List<DetectedIssue>()
            };
        }

        private SnapshotStore CreateStore(int maxKept = 100) =>
            new SnapshotStore(_tempDir, maxKept);

        [Fact]
        public void Throws_on_blank_directory()
        {
            Assert.Throws<ArgumentException>(() => new SnapshotStore("", 10));
        }

        [Fact]
        public void Throws_on_zero_or_negative_maxKept()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotStore(_tempDir, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotStore(_tempDir, -1));
        }

        [Fact]
        public void Save_creates_directory_if_absent()
        {
            Assert.False(Directory.Exists(_tempDir));
            var store = CreateStore();

            store.Save(Snapshot());

            Assert.True(Directory.Exists(_tempDir));
        }

        [Fact]
        public void Save_writes_file_with_expected_naming_convention()
        {
            var store = CreateStore();

            var path = store.Save(Snapshot());

            var fileName = Path.GetFileName(path);
            Assert.StartsWith("ADReplic-", fileName);
            Assert.EndsWith(".json", fileName);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Save_returns_full_path()
        {
            var store = CreateStore();
            var path = store.Save(Snapshot());
            Assert.True(Path.IsPathRooted(path));
            Assert.Equal(_tempDir, Path.GetDirectoryName(path));
        }

        [Fact]
        public void Throws_when_saving_null_snapshot()
        {
            Assert.Throws<ArgumentNullException>(() => CreateStore().Save(null));
        }

        [Fact]
        public void List_returns_empty_when_directory_does_not_exist()
        {
            var store = CreateStore();
            // Pas de Save : le dossier n'existe pas.
            Assert.Empty(store.List());
        }

        [Fact]
        public void List_returns_files_sorted_by_capture_time_descending()
        {
            var store = CreateStore();
            store.Save(Snapshot(new DateTime(2026, 5, 1, 10, 0, 0)));
            store.Save(Snapshot(new DateTime(2026, 5, 23, 10, 0, 0)));
            store.Save(Snapshot(new DateTime(2026, 5, 15, 10, 0, 0)));

            var files = store.List();

            Assert.Equal(3, files.Count);
            Assert.True(files[0].CapturedAt > files[1].CapturedAt);
            Assert.True(files[1].CapturedAt > files[2].CapturedAt);
        }

        [Fact]
        public void List_ignores_files_not_matching_convention()
        {
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(Path.Combine(_tempDir, "autre.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDir, "ADReplic-pas-une-date.json"), "{}");

            var store = CreateStore();
            store.Save(Snapshot());

            // Seul le fichier sauvegarde par le store doit apparaitre.
            Assert.Single(store.List());
        }

        [Fact]
        public void Rotation_keeps_only_the_n_most_recent_files()
        {
            var store = CreateStore(maxKept: 3);

            // 5 sauvegardes : seules les 3 plus récentes doivent survivre.
            for (int i = 0; i < 5; i++)
            {
                store.Save(Snapshot(new DateTime(2026, 5, 1 + i, 10, 0, 0)));
            }

            var files = store.List();
            Assert.Equal(3, files.Count);

            // Les 3 conservées doivent être les plus récentes.
            var capturedDates = files.Select(f => f.CapturedAt.Date).ToList();
            Assert.Contains(new DateTime(2026, 5, 5), capturedDates);
            Assert.Contains(new DateTime(2026, 5, 4), capturedDates);
            Assert.Contains(new DateTime(2026, 5, 3), capturedDates);
        }

        [Fact]
        public void Load_round_trip_recovers_basic_fields()
        {
            var store = CreateStore();
            var snapshot = Snapshot(forest: "exemple.local");
            snapshot.DomainControllers = new[]
            {
                new DomainControllerInfo { HostName = "DC01", Domain = "exemple.local", OSVersion = "Windows Server 2022", IsGlobalCatalog = true }
            };

            var path = store.Save(snapshot);
            var fileName = Path.GetFileName(path);

            var loaded = store.Load(fileName);

            Assert.Equal("exemple.local", loaded.ForestName);
            Assert.Single(loaded.DomainControllers);
            Assert.Equal("DC01", loaded.DomainControllers[0].HostName);
            Assert.True(loaded.DomainControllers[0].IsGlobalCatalog);
        }

        [Fact]
        public void Load_accepts_absolute_path()
        {
            var store = CreateStore();
            var path = store.Save(Snapshot());

            var loaded = store.Load(path);

            Assert.NotNull(loaded);
        }

        [Fact]
        public void Load_throws_when_file_does_not_exist()
        {
            var store = CreateStore();
            Directory.CreateDirectory(_tempDir);
            Assert.Throws<FileNotFoundException>(() => store.Load("ADReplic-2099-01-01_000000-000.json"));
        }

        [Fact]
        public void Two_saves_within_same_millisecond_do_not_overwrite()
        {
            var store = CreateStore();
            var sameInstant = new DateTime(2026, 5, 23, 14, 0, 0, DateTimeKind.Local);

            var path1 = store.Save(Snapshot(sameInstant));
            var path2 = store.Save(Snapshot(sameInstant));

            Assert.NotEqual(path1, path2);
            Assert.True(File.Exists(path1));
            Assert.True(File.Exists(path2));
        }
    }
}
