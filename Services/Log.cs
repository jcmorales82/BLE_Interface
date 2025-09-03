using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using BLE_Interface.Helpers;

namespace BLE_Interface.Services.Logging
{
    /// <summary>
    /// Central app logger with bounded memory and batched UI updates.
    /// Use: Log.Info("..."), Log.Warn("..."), Log.Error("..."), Log.Debug("...")
    /// Bind to Log.Items in the UI (we'll wire that in the next step).
    /// </summary>
    public static class Log
    {
        // Keep only the last N lines in memory/UI.
        public static BoundedObservableCollection<string> Items { get; } =
            new BoundedObservableCollection<string>(capacity: 2000);

        private static readonly UiBatcher<string> _batcher;
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

        static Log()
        {
            // Use the app's Dispatcher so we can flush to the UI safely.
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            _batcher = new UiBatcher<string>(
                dispatcher,
                interval: TimeSpan.FromMilliseconds(100),     // flush ~10 times/sec
                onFlush: batch => Items.AddRange(batch)       // Items is bounded, so old lines drop off
            );
        }

        public static void Info(string message) => Enqueue("INFO", message);
        public static void Warn(string message) => Enqueue("WARN", message);
        public static void Error(string message) => Enqueue("ERROR", message);
        public static void Debug(string message) => Enqueue("DEBUG", message);

        private static void Enqueue(string level, string message)
        {
            var line = $"{DateTime.Now:G} [{level}] {message}";
            _batcher.Post(line);
        }
    }
}
