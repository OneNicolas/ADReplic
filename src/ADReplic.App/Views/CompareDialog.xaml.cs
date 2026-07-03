using System.Windows;
using ADReplic.App.ViewModels;

namespace ADReplic.App.Views
{
    /// <summary>
    /// Dialog de sélection des deux fichiers JSON à comparer. Le calcul du diff
    /// et la gestion d'erreur vivent dans le <see cref="CompareViewModel"/> ;
    /// la fenêtre ne fait que présenter et brancher les commandes.
    /// </summary>
    public partial class CompareDialog : Window
    {
        public CompareDialog(CompareViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
