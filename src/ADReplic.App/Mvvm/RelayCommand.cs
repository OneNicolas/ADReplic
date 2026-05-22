using System;
using System.Windows.Input;

namespace ADReplic.App.Mvvm
{
    /// <summary>
    /// Commande synchrone paramétrée. Couvre les actions UI rapides
    /// qui n'ont pas besoin de Task (exports, navigation, toggles).
    /// </summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
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
            => _canExecute?.Invoke(Cast(parameter)) ?? true;

        public void Execute(object parameter)
            => _execute(Cast(parameter));

        private static T Cast(object parameter)
        {
            if (parameter == null && default(T) == null) return default;
            return (T)parameter;
        }
    }
}
