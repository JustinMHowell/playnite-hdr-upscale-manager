using System;
using System.Windows.Input;

namespace HdrUpscaleManager.UI
{
    /* Minimal hand-rolled ICommand — no MVVM toolkit is referenced in this net462 project.
       Execute wraps async command bodies as fire-and-forget; every command body is expected to
       catch its own exceptions (see ManagerViewModel) so nothing here needs to. */
    public class RelayCommand : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool> canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        /* Tied to CommandManager.RequerySuggested (the classic RelayCommand trick) so CanExecute
           re-evaluates automatically on focus/keypress/click — including gamepad-driven
           navigation, since Playnite translates D-pad input into the same focus/key events. */
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => execute();
    }
}
