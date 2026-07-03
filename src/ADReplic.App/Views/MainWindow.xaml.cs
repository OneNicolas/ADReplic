using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
                vm.SaveFilePicker      = AskSavePath;
                vm.OnEditCredentials   = () => ShowCredentialsDialog(vm);
                vm.OnOpenCompareDialog = () => ShowCompareDialog(vm);
                vm.OnShowAlert         = ShowToast;

                // Le sous-VM Compare a besoin de son propre OpenFileDialog (filtré JSON).
                vm.Compare.OnPickFile = AskPickJsonPath;
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

        private void ShowCompareDialog(MainViewModel vm)
        {
            var dialog = new CompareDialog(vm.Compare) { Owner = this };
            dialog.ShowDialog();
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

        /// <summary>
        /// Ouvre un OpenFileDialog filtré sur les fichiers JSON pour la
        /// sélection des audits à comparer. Retourne null si annulé.
        /// </summary>
        private string AskPickJsonPath()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Audits ADReplic (*.json)|*.json|Tous les fichiers (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
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

        // -------- Badge score : ouvre le popover historique --------

        private void OnScoreBadgeClick(object sender, MouseButtonEventArgs e)
        {
            HistoryPopup.IsOpen = !HistoryPopup.IsOpen;
        }

        // -------- Toast d'alerte : animation fade in / 8s / fade out --------

        /// <summary>
        /// Affiche le toast avec animation. Si un toast est déjà visible, l'animation
        /// est interrompue et reprise à zéro avec le nouveau texte. Permet d'enchaîner
        /// plusieurs alertes successives sans accumulation visuelle.
        /// </summary>
        private void ShowToast()
        {
            if (!(DataContext is MainViewModel vm)) return;

            ToastText.Text = vm.PendingAlertSummary ?? "Changement significatif détecté.";
            ToastBorder.Visibility = Visibility.Visible;

            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,  KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame   (1,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(7.7))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame   (0,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(8))));
            animation.Completed += (s, e) =>
            {
                // Au cas où une nouvelle animation a déjà été lancée entre-temps,
                // on ne cache le toast que s'il est toujours à opacité 0.
                if (ToastBorder.Opacity < 0.01) ToastBorder.Visibility = Visibility.Collapsed;
            };
            ToastBorder.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>Clic sur le toast : acquitte l'alerte puis cache immédiatement.</summary>
        private void OnToastClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.AcknowledgeAlertCommand.CanExecute(null))
            {
                vm.AcknowledgeAlertCommand.Execute(null);
            }
            // Arret immediat de l'animation en cours et masquage.
            ToastBorder.BeginAnimation(OpacityProperty, null);
            ToastBorder.Opacity = 0;
            ToastBorder.Visibility = Visibility.Collapsed;
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
                "• Exporter : produit un rapport CSV, JSON ou HTML autonome.\n" +
                "• Comparer deux audits : ouvre deux JSON existants pour visualiser les différences.\n" +
                "• Surveillance : audit récurrent avec alerte sur changement significatif.\n\n" +
                "Astuce : F5 pour actualiser, Alt+F4 pour quitter.",
                "Guide de démarrage",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnDocsClick(object sender, RoutedEventArgs e)
        {
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
