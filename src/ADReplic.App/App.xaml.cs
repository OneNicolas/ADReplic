using System.Windows;
using ADReplic.App.Themes;

namespace ADReplic.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Charge le thème persisté avant que la fenêtre principale ne se construise.
            ThemeManager.LoadFromSettings();
            base.OnStartup(e);
        }
    }
}
