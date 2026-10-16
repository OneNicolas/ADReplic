using System;
using System.IO;
using ADReplic.Core.Monitoring;
using ADReplic.Core.Security;
using Xunit;

namespace ADReplic.Core.Tests.Security
{
    /// <summary>
    /// Identifiants mémorisés (v0.9.0) : la cible du Gestionnaire d'identifiants Windows
    /// est dérivée de la forêt, de façon stable quelle que soit la casse ou le point final
    /// saisi, pour que la même entrée soit retrouvée d'un lancement à l'autre.
    /// </summary>
    public class CredentialTargetsTests
    {
        [Theory]
        [InlineData("exemple.local", "ADReplic:exemple.local")]
        [InlineData("  EXEMPLE.LOCAL.  ", "ADReplic:exemple.local")]
        [InlineData(null, "ADReplic:(foret-courante)")]
        [InlineData("   ", "ADReplic:(foret-courante)")]
        public void Target_is_stable_for_a_forest(string forest, string expected)
        {
            Assert.Equal(expected, CredentialTargets.ForForest(forest));
        }

        [Fact]
        public void Targets_of_two_forests_differ()
        {
            Assert.NotEqual(CredentialTargets.ForForest("a.local"), CredentialTargets.ForForest("b.local"));
        }
    }

    /// <summary>Aller-retour réel sur le Gestionnaire d'identifiants Windows, entrée de test auto-nettoyée.</summary>
    public class WindowsCredentialStoreTests
    {
        [Fact]
        public void Save_read_overwrite_and_delete_round_trip()
        {
            var store = new WindowsCredentialStore();
            var target = "ADReplic-Test:" + Guid.NewGuid().ToString("N");
            try
            {
                Assert.False(store.TryRead(target, out _));

                store.Save(target, "EXEMPLE\\audit", "P@ss-éè€1");
                Assert.True(store.TryRead(target, out var first));
                Assert.Equal("EXEMPLE\\audit", first.UserName);
                Assert.Equal("P@ss-éè€1", first.Password);

                store.Save(target, "audit@exemple.local", "nouveau");
                Assert.True(store.TryRead(target, out var second));
                Assert.Equal("audit@exemple.local", second.UserName);
                Assert.Equal("nouveau", second.Password);

                Assert.True(store.Delete(target));
                Assert.False(store.Delete(target));
                Assert.False(store.TryRead(target, out _));
            }
            finally
            {
                store.Delete(target);
            }
        }
    }

    public class MonitoringSettingsLastForestTests
    {
        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "ADReplic-Tests-" + Guid.NewGuid().ToString("N"), "monitoring.json");

        [Fact]
        public void Last_target_forest_round_trips_and_defaults_to_null()
        {
            var path = TempPath();
            var store = new MonitoringSettingsStore(path);

            Assert.Null(store.Load().LastTargetForest);

            store.Save(new MonitoringSettings { IntervalMinutes = 10, LastTargetForest = "exemple.local" });
            var reloaded = store.Load();

            Assert.Equal("exemple.local", reloaded.LastTargetForest);
            Assert.Equal(10, reloaded.IntervalMinutes);
            Directory.Delete(Path.GetDirectoryName(path), recursive: true);
        }

        [Fact]
        public void Older_settings_file_without_the_field_still_loads()
        {
            var path = TempPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"IntervalMinutes\":30}");

            var settings = new MonitoringSettingsStore(path).Load();

            Assert.Equal(30, settings.IntervalMinutes);
            Assert.Null(settings.LastTargetForest);
            Directory.Delete(Path.GetDirectoryName(path), recursive: true);
        }
    }
}
