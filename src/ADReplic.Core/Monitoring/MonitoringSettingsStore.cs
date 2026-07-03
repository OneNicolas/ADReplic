using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ADReplic.Core.Monitoring
{
    /// <summary>
    /// Charge et persiste les <see cref="MonitoringSettings"/> dans un fichier
    /// JSON sous <c>%APPDATA%\ADReplic\monitoring.json</c>. Tolérant aux erreurs :
    /// un fichier corrompu ou absent retourne les valeurs par défaut.
    /// </summary>
    public sealed class MonitoringSettingsStore
    {
        private const string DefaultFileName = "monitoring.json";

        private readonly string _path;

        public MonitoringSettingsStore() : this(GetDefaultPath()) { }

        public MonitoringSettingsStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Chemin requis.", nameof(path));
            _path = path;
        }

        public string Path => _path;

        /// <summary>
        /// Lit le fichier. En cas d'absence ou de format invalide, retourne une
        /// instance avec les valeurs par défaut (jamais null).
        /// </summary>
        public MonitoringSettings Load()
        {
            if (!File.Exists(_path)) return new MonitoringSettings();

            try
            {
                var json = File.ReadAllText(_path, Encoding.UTF8);
                var settings = new JavaScriptSerializer().Deserialize<MonitoringSettings>(json);
                return settings ?? new MonitoringSettings();
            }
            catch
            {
                // Fichier corrompu : on revient aux valeurs par defaut plutot
                // que de bloquer le lancement de l'application.
                return new MonitoringSettings();
            }
        }

        public void Save(MonitoringSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = new JavaScriptSerializer().Serialize(settings);
            File.WriteAllText(_path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string GetDefaultPath() => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ADReplic", DefaultFileName);
    }
}
