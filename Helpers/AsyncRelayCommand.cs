using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BLE_Interface.Helpers
{
    public sealed class AsyncRelayCommand : ICommand
    {
        readonly Func<CancellationToken, Task> _execute;
        readonly Func<bool> _canExecute;
        CancellationTokenSource _cts;
        bool _running;

        public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null)
        { _execute = execute; _canExecute = canExecute; }

        public bool CanExecute(object p) => !_running && (_canExecute?.Invoke() ?? true);
        public event EventHandler CanExecuteChanged;
        public async void Execute(object p)
        {
            if (!CanExecute(p)) return;
            _running = true; _cts = new CancellationTokenSource();
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try { await _execute(_cts.Token).ConfigureAwait(true); }
            finally { _running = false; _cts.Dispose(); _cts = null; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
        }
        public void Cancel() => _cts?.Cancel();
    }
}