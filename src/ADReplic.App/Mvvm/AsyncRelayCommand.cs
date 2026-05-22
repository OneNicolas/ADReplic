using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ADReplic.App.Mvvm
{
    /// <summary>
    /// Commande asynchrone qui désactive automatiquement son CanExecute pendant
    /// l'exécution. Couvre 95% des besoins MVVM sans framework externe.
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter)
        {
            if (_isRunning) return false;
            return _canExecute?.Invoke() ?? true;
        }

        public async void Execute(object parameter)
        {
            _isRunning = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _execute();
            }
            finally
            {
                _isRunning = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
