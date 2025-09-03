using System;
using System.Threading;

namespace BLE_Interface.Helpers
{
    /// <summary>
    /// Small helper to ensure we unsubscribe/cleanup reliably.
    /// Wrap any "+=" with an ActionDisposable that does the corresponding "-=".
    /// </summary>
    public sealed class ActionDisposable : IDisposable
    {
        private Action _dispose;
        public ActionDisposable(Action dispose) => _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        public void Dispose()
        {
            var d = Interlocked.Exchange(ref _dispose, null);
            d?.Invoke();
        }
    }
}
