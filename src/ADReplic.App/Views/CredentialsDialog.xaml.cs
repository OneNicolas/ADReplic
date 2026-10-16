using System;
using System.Windows;
using System.Windows.Controls;

namespace ADReplic.App.Views
{
    /// <summary>
    /// Dialog modal de saisie de la forêt cible et des identifiants alternatifs.
    /// Le mot de passe transite uniquement par PasswordBox (jamais en clair dans le XAML).
    /// v0.9.0 : forêt cible dans le dialogue (elle nomme l'entrée du coffre), case
    /// « Mémoriser sur ce poste » et bouton « Oublier » (Gestionnaire d'identifiants Windows).
    /// </summary>
    public partial class CredentialsDialog : Window
    {
        private readonly Func<string, bool> _hasSavedCredentialsFor;
        private bool _forestWasDeduced;

        /// <summary>Forêt cible validée (null = forêt du poste), reprise dans la barre principale.</summary>
        public string ForestName { get; private set; }
        public string UserName { get; private set; }
        public string Password { get; private set; }

        /// <summary>Vrai si l'utilisateur veut mémoriser ces identifiants pour la forêt cible.</summary>
        public bool Remember { get; private set; }

        /// <summary>Vrai si l'utilisateur a demandé à supprimer l'entrée mémorisée pour la forêt saisie.</summary>
        public bool Forget { get; private set; }

        public CredentialsDialog(string initialForest, string initialUser, Func<string, bool> hasSavedCredentialsFor)
        {
            _hasSavedCredentialsFor = hasSavedCredentialsFor ?? (_ => false);
            InitializeComponent();
            ForestBox.Text = initialForest ?? string.Empty;
            UserBox.Text = initialUser ?? string.Empty;
            RefreshRememberState();
            if (string.IsNullOrEmpty(initialForest)) ForestBox.Focus();
            else if (string.IsNullOrEmpty(initialUser)) UserBox.Focus();
            else PassBox.Focus();
        }

        private string CurrentForest => string.IsNullOrWhiteSpace(ForestBox.Text) ? null : ForestBox.Text.Trim();

        /// <summary>Case cochée et « Oublier » visible dès qu'une entrée existe pour la forêt saisie.</summary>
        private void RefreshRememberState()
        {
            var forest = CurrentForest;
            var hasSaved = _hasSavedCredentialsFor(forest);
            RememberBox.Content = "Mémoriser sur ce poste pour " + (forest ?? "la forêt du poste");
            if (hasSaved) RememberBox.IsChecked = true;
            ForgetButton.Visibility = hasSaved ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnForestChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _forestWasDeduced = false;
            RefreshRememberState();
        }

        /// <summary>Forêt déduite du suffixe UPN tant que l'utilisateur ne l'a pas saisie lui-même.</summary>
        private void OnUserChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (!string.IsNullOrWhiteSpace(ForestBox.Text) && !_forestWasDeduced) return;

            var suffix = UpnSuffix(UserBox.Text);
            if (suffix == null) return;
            ForestBox.Text = suffix;
            _forestWasDeduced = true;
            RefreshRememberState();
        }

        private static string UpnSuffix(string user)
        {
            var at = user?.LastIndexOf('@') ?? -1;
            if (at < 0 || at == user.Length - 1) return null;
            var suffix = user.Substring(at + 1).Trim();
            return suffix.IndexOf('.') > 0 ? suffix : null;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            ForestName = CurrentForest ?? UpnSuffix(UserBox.Text);
            UserName = UserBox.Text?.Trim();
            Password = PassBox.Password;
            Remember = RememberBox.IsChecked == true;
            DialogResult = true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            UserBox.Clear();
            PassBox.Clear();
            ForestName = CurrentForest;
            UserName = null;
            Password = null;
            Remember = false;
            DialogResult = true;
        }

        private void OnForgetClick(object sender, RoutedEventArgs e)
        {
            ForestName = CurrentForest;
            Forget = true;
            DialogResult = true;
        }
    }
}
