using System;
using System.Windows.Input;

#nullable enable


namespace FileCopy
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _exec;
        private readonly Predicate<object> _can;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _exec = execute ?? throw new ArgumentNullException(nameof(execute));
            _can  = canExecute;
        }

        public bool CanExecute(object? parameter) => _can?.Invoke(parameter!) ?? true;
        public void Execute(object? parameter)    => _exec(parameter!);

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
