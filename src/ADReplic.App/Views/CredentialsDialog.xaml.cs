using System.Windows;

namespace ADReplic.App.Views
{
    /// <summary>
    /// Dialog modal de saisie des identifiants alternatifs.
    /// Le mot de passe transite uniquement par PasswordBox (jamais en clair dans le XAML).
    /// v0.9.0 : case « Mémoriser sur ce poste » et bouton « Oublier » (coffre Windows).
    /// </summary>
    public partial class CredentialsDialog : Window
    {
        public string UserName { get; private set; }
        public string Password { get; private set; }

        /// <summary>Vrai si l'utilisateur veut mémoriser ces identifiants pour la forêt cible.</summary>
        public bool Remember { get; private set; }

        /// <summary>Vrai si l'utilisateur a demandé à supprimer l'entrée mémorisée.</summary>
        public bool Forget { get; private set; }

        public CredentialsDialog(string initialUser, bool hasSavedCredentials, string forestLabel)
        {
            InitializeComponent();
            UserBox.Text = initialUser ?? string.Empty;
            // Entrée existante : la case est cochée (re-saisir = mettre à jour), « Oublier » visible.
            RememberBox.IsChecked = hasSavedCredentials;
            RememberBox.Content = "Mémoriser sur ce poste pour " + forestLabel;
            ForgetButton.Visibility = hasSavedCredentials ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrEmpty(initialUser)) UserBox.Focus(); else PassBox.Focus();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            UserName = UserBox.Text?.Trim();
            Password = PassBox.Password;
            Remember = RememberBox.IsChecked == true;
            DialogResult = true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            UserBox.Clear();
            PassBox.Clear();
            UserName = null;
            Password = null;
            Remember = false;
            DialogResult = true;
        }

        private void OnForgetClick(object sender, RoutedEventArgs e)
        {
            Forget = true;
            DialogResult = true;
        }
    }
}
