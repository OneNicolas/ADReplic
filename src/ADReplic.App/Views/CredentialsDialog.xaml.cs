using System.Windows;

namespace ADReplic.App.Views
{
    /// <summary>
    /// Dialog modal de saisie des identifiants alternatifs.
    /// Le mot de passe transite uniquement par PasswordBox (jamais en clair dans le XAML).
    /// </summary>
    public partial class CredentialsDialog : Window
    {
        public string UserName { get; private set; }
        public string Password { get; private set; }

        public CredentialsDialog(string initialUser)
        {
            InitializeComponent();
            UserBox.Text = initialUser ?? string.Empty;
            UserBox.Focus();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            UserName = UserBox.Text?.Trim();
            Password = PassBox.Password;
            DialogResult = true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            UserBox.Clear();
            PassBox.Clear();
            UserName = null;
            Password = null;
            DialogResult = true;
        }
    }
}
