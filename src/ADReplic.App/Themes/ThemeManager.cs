using System;
using System.IO;
using System.Windows;

namespace ADReplic.App.Themes
{
    public enum AppTheme { Light, Dark }

    /// <summary>
    /// Gère le thème actif de l'application : permute le ResourceDictionary
    /// de palette à chaud et persiste le choix utilisateur dans %APPDATA%.
    /// </summary>
    public static class ThemeManager
    {
        private const string LightThemeUri = "pack://application:,,,/Themes/LightTheme.xaml";
        private const string DarkThemeUri  = "pack://application:,,,/Themes/DarkTheme.xaml";

        // Nom de fichier court pour repérer le dictionnaire de thème déjà chargé
        // dans Application.Current.Resources.MergedDictionaries.
        private const string LightFileName = "LightTheme.xaml";
        private const string DarkFileName  = "DarkTheme.xaml";

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        /// <summary>Applique un thème et le persiste.</summary>
        public static void Apply(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null) return;

            // Retire l'ancien dictionnaire de thème pour éviter l'accumulation.
            RemoveExistingThemes(app.Resources.MergedDictionaries);

            var newDict = new ResourceDictionary
            {
                Source = new Uri(theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri, UriKind.Absolute)
            };
            // On insère le thème en premier pour que les autres dictionnaires
            // (styles globaux) puissent référencer ses brushes via StaticResource.
            app.Resources.MergedDictionaries.Insert(0, newDict);

            Current = theme;
            SaveChoice(theme);
        }

        /// <summary>Bascule entre clair et sombre.</summary>
        public static void Toggle() =>
            Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

        /// <summary>Charge le thème persisté au démarrage (ou Light par défaut).</summary>
        public static void LoadFromSettings()
        {
            Apply(ReadChoice());
        }

        private static void RemoveExistingThemes(System.Collections.ObjectModel.Collection<ResourceDictionary> dicts)
        {
            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source?.OriginalString ?? "";
                if (src.EndsWith(LightFileName, StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith(DarkFileName,  StringComparison.OrdinalIgnoreCase))
                {
                    dicts.RemoveAt(i);
                }
            }
        }

        // -- Persistance simple (fichier texte d'une ligne dans %APPDATA%) ------

        private static string SettingsFilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ADReplic");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "theme.txt");
            }
        }

        private static void SaveChoice(AppTheme theme)
        {
            try { File.WriteAllText(SettingsFilePath, theme.ToString()); }
            catch { /* échec silencieux : le thème reste appliqué pour la session */ }
        }

        private static AppTheme ReadChoice()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var raw = File.ReadAllText(SettingsFilePath).Trim();
                    if (Enum.TryParse<AppTheme>(raw, ignoreCase: true, out var theme))
                        return theme;
                }
            }
            catch { /* on retombe sur le défaut */ }
            return AppTheme.Light;
        }
    }
}
