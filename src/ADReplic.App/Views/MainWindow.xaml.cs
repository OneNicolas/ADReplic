using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ADReplic.App.Themes;
using ADReplic.App.ViewModels;
using Microsoft.Win32;

namespace ADReplic.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SetupWindowIcon();
        }

        /// <summary>
        /// Convertit le DrawingImage vectoriel défini dans App.xaml en BitmapSource
        /// pour pouvoir l'utiliser comme Window.Icon. Cette astuce évite de devoir
        /// embarquer un fichier .ico binaire dans le projet.
        /// </summary>
        private void SetupWindowIcon()
        {
            try
            {
                if (Application.Current?.Resources["AppIcon"] is DrawingImage drawing)
                {
                    Icon = RenderDrawingToBitmap(drawing, 64);
                }
            }
            catch
            {
                // Pas d'icône, pas grave : la fenêtre garde l'icône par défaut.
            }
        }

        private static BitmapSource RenderDrawingToBitmap(DrawingImage drawing, int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(drawing, new Rect(0, 0, size, size));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SaveFilePicker = AskSavePath;
                vm.OnEditCredentials = () => ShowCredentialsDialog(vm);
            }
        }

        private void ShowCredentialsDialog(MainViewModel vm)
        {
            var dialog = new CredentialsDialog(vm.CredentialUserName) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                vm.ApplyCredentials(dialog.UserName, dialog.Password);
            }
        }

        /// <summary>
        /// Le ViewModel délègue ici l'ouverture du dialogue Win32 :
        /// la couche UI reste responsable de l'I/O système.
        /// </summary>
        private string AskSavePath(string format, string suggestedName)
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedName,
                Filter = BuildFilter(format),
                OverwritePrompt = true,
                AddExtension = true
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private static string BuildFilter(string format)
        {
            switch (format)
            {
                case "CSV":  return "Fichiers CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*";
                case "JSON": return "Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*";
                case "HTML": return "Rapports HTML (*.html)|*.html|Tous les fichiers (*.*)|*.*";
                default:     return "Tous les fichiers (*.*)|*.*";
            }
        }

        private void OnExportButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.Placement = PlacementMode.Bottom;
                fe.ContextMenu.IsOpen = true;
            }
        }

        // -------- Menu : Aide --------

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            new AboutDialog { Owner = this }.ShowDialog();
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this,
                "Guide rapide :\n\n" +
                "• Actualiser : lance l'audit de la forêt courante.\n" +
                "• Forêt cible : tapez le FQDN d'une autre forêt à interroger.\n" +
                "• Identifiants : définissez un compte alternatif (UPN ou DOMAINE\\user).\n" +
                "• Exporter : produit un rapport CSV, JSON ou HTML autonome.\n\n" +
                "Astuce : F5 pour actualiser, Alt+F4 pour quitter.",
                "Guide de démarrage",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnDocsClick(object sender, RoutedEventArgs e)
        {
            // Ouvre la page GitHub du projet dans le navigateur par défaut.
            TryOpenUrl("https://github.com/OneNicolas/ADReplic");
        }

        private void OnQuitClick(object sender, RoutedEventArgs e) => Close();

        // -------- Menu : Outils > Thème --------

        private void OnThemeLightClick(object sender, RoutedEventArgs e) =>
            ThemeManager.Apply(AppTheme.Light);

        private void OnThemeDarkClick(object sender, RoutedEventArgs e) =>
            ThemeManager.Apply(AppTheme.Dark);

        private void TryOpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossible d'ouvrir le navigateur : " + ex.Message,
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
