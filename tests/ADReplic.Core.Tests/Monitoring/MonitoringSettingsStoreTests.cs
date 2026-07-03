using System;
using System.IO;
using ADReplic.Core.Monitoring;
using Xunit;

namespace ADReplic.Core.Tests.Monitoring
{
    public class MonitoringSettingsStoreTests : IDisposable
    {
        private readonly string _tempFile;

        public MonitoringSettingsStoreTests()
        {
            _tempFile = Path.Combine(
                Path.GetTempPath(),
                "adreplic-monitoring-" + Guid.NewGuid().ToString("N") + ".json");
        }

        public void Dispose()
        {
            try { if (File.Exists(_tempFile)) File.Delete(_tempFile); }
            catch { /* best-effort */ }
        }

        [Fact]
        public void Default_settings_when_file_does_not_exist()
        {
            var store = new MonitoringSettingsStore(_tempFile);

            var settings = store.Load();

            Assert.NotNull(settings);
            Assert.Equal(15, settings.IntervalMinutes);
        }

        [Fact]
        public void Default_settings_when_file_is_corrupted()
        {
            File.WriteAllText(_tempFile, "{ this is not valid json");
            var store = new MonitoringSettingsStore(_tempFile);

            var settings = store.Load();

            Assert.NotNull(settings);
            Assert.Equal(15, settings.IntervalMinutes);
        }

        [Fact]
        public void Save_then_load_preserves_interval()
        {
            var store = new MonitoringSettingsStore(_tempFile);

            store.Save(new MonitoringSettings { IntervalMinutes = 30 });
            var loaded = store.Load();

            Assert.Equal(30, loaded.IntervalMinutes);
        }

        [Fact]
        public void Save_creates_parent_directory_if_absent()
        {
            var nestedPath = Path.Combine(
                Path.GetTempPath(),
                "adreplic-nested-" + Guid.NewGuid().ToString("N"),
                "monitoring.json");
            try
            {
                var store = new MonitoringSettingsStore(nestedPath);

                store.Save(new MonitoringSettings { IntervalMinutes = 60 });

                Assert.True(File.Exists(nestedPath));
            }
            finally
            {
                var dir = Path.GetDirectoryName(nestedPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, recursive: true); } catch { }
                }
            }
        }

        [Fact]
        public void Throws_on_blank_path()
        {
            Assert.Throws<ArgumentException>(() => new MonitoringSettingsStore(""));
        }

        [Fact]
        public void Throws_on_null_settings()
        {
            var store = new MonitoringSettingsStore(_tempFile);
            Assert.Throws<ArgumentNullException>(() => store.Save(null));
        }
    }
}
