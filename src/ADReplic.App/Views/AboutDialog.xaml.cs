using System.Reflection;
using System.Windows;

namespace ADReplic.App.Views
{
    /// <summary>
    /// Fenêtre "À propos" qui affiche la version courante lue depuis l'assembly.
    /// La version est définie une seule fois dans Directory.Build.props.
    /// </summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            VersionText.Text = "Version " + GetAssemblyVersion();
        }

        private static string GetAssemblyVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            // AssemblyInformationalVersion suit MSBuild Version, donc reflète Directory.Build.props.
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (info != null && !string.IsNullOrEmpty(info.InformationalVersion))
            {
                return info.InformationalVersion;
            }
            return asm.GetName().Version?.ToString() ?? "?";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
