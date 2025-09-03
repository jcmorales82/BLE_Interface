using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Threading;

namespace BLE_Interface.Helpers
{
    /// <summary>
    /// Queues items from any thread and flushes them to the UI at a fixed rate.
    /// Reduces per-item UI churn (big performance win for logs, charts, device list).
    /// </summary>
    public sealed class UiBatcher<T> : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly Action<IReadOnlyList<T>> _onFlush;

        public UiBatcher(Dispatcher dispatcher, TimeSpan interval, Action<IReadOnlyList<T>> onFlush)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _onFlush = onFlush ?? throw new ArgumentNullException(nameof(onFlush));

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = interval
            };
            _timer.Tick += (_, __) =>
            {
                var list = new List<T>(512);
                while (_queue.TryDequeue(out var item)) list.Add(item);
                if (list.Count > 0) _onFlush(list);
            };
            _timer.Start();
        }

        public void Post(T item) => _queue.Enqueue(item);

        public void Dispose() => _timer.Stop();
    }
}