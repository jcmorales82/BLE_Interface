using System;
using System.Threading;

namespace BLE_Interface.Helpers
{
    /// <summary>
    /// Throttles rapid method calls to a maximum frequency.
    /// Useful for preventing chart updates from overwhelming the UI thread.
    /// Thread-safe and efficient.
    /// </summary>
    public sealed class ChartThrottler : IDisposable
    {
        private readonly Action _action;
        private readonly int _intervalMs;
        private readonly Timer _timer;
        private int _pendingInvocation;
        private long _lastExecutionTicks;

        /// <summary>
        /// Creates a throttler that limits execution to once per interval.
        /// </summary>
        /// <param name="action">Action to throttle</param>
        /// <param name="intervalMs">Minimum milliseconds between executions (e.g., 16ms = ~60 FPS)</param>
        public ChartThrottler(Action action, int intervalMs)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _intervalMs = intervalMs;
            _lastExecutionTicks = DateTime.UtcNow.Ticks;

            // Timer fires every interval/2 to check if we should execute
            _timer = new Timer(OnTimerTick, null, intervalMs / 2, intervalMs / 2);
        }

        /// <summary>
        /// Request execution. May execute immediately or be deferred based on throttling.
        /// </summary>
        public void Invoke()
        {
            // Mark that we have a pending invocation
            Interlocked.Exchange(ref _pendingInvocation, 1);

            // Check if enough time has passed
            var now = DateTime.UtcNow.Ticks;
            var elapsed = TimeSpan.FromTicks(now - Interlocked.Read(ref _lastExecutionTicks));

            if (elapsed.TotalMilliseconds >= _intervalMs)
            {
                TryExecute();
            }
        }

        private void OnTimerTick(object state)
        {
            TryExecute();
        }

        private void TryExecute()
        {
            // Check if we have a pending invocation
            if (Interlocked.CompareExchange(ref _pendingInvocation, 0, 1) == 1)
            {
                var now = DateTime.UtcNow.Ticks;
                var elapsed = TimeSpan.FromTicks(now - Interlocked.Read(ref _lastExecutionTicks));

                // Only execute if enough time has passed
                if (elapsed.TotalMilliseconds >= _intervalMs)
                {
                    Interlocked.Exchange(ref _lastExecutionTicks, now);

                    try
                    {
                        _action();
                    }
                    catch
                    {
                        // Swallow exceptions to prevent timer from stopping
                    }
                }
                else
                {
                    // Not enough time passed, restore the pending flag
                    Interlocked.Exchange(ref _pendingInvocation, 1);
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}