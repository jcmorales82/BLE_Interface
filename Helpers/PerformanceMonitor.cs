using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

namespace BLE_Interface.Helpers
{
    /// <summary>
    /// Simple performance monitor to track frame rates, event frequencies, and timing.
    /// Useful for diagnosing performance issues during development.
    /// </summary>
    public static class PerformanceMonitor
    {
        private static readonly ConcurrentDictionary<string, MetricData> _metrics = new ConcurrentDictionary<string, MetricData>();

        /// <summary>
        /// Record an event occurrence for rate tracking.
        /// </summary>
        public static void RecordEvent(string name)
        {
            var metric = _metrics.GetOrAdd(name, _ => new MetricData());
            metric.RecordEvent();
        }

        /// <summary>
        /// Time an operation and record its duration.
        /// Usage: using (PerformanceMonitor.Time("MyOperation")) { ... }
        /// </summary>
        public static IDisposable Time(string name)
        {
            return new TimingScope(name);
        }

        /// <summary>
        /// Get current statistics for a metric.
        /// </summary>
        public static string GetStats(string name)
        {
            if (!_metrics.TryGetValue(name, out var metric))
                return $"{name}: No data";

            return metric.GetStats(name);
        }

        /// <summary>
        /// Get all statistics as a formatted string.
        /// </summary>
        public static string GetAllStats()
        {
            var lines = _metrics.OrderBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value.GetStats(kvp.Key));

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Reset all metrics.
        /// </summary>
        public static void Reset()
        {
            _metrics.Clear();
        }

        private class MetricData
        {
            private readonly ConcurrentQueue<long> _eventTicks = new ConcurrentQueue<long>();
            private readonly ConcurrentQueue<long> _durations = new ConcurrentQueue<long>();
            private const int MaxSamples = 100;

            public void RecordEvent()
            {
                _eventTicks.Enqueue(DateTime.UtcNow.Ticks);

                // Trim old samples
                while (_eventTicks.Count > MaxSamples)
                    _eventTicks.TryDequeue(out _);
            }

            public void RecordDuration(long durationMs)
            {
                _durations.Enqueue(durationMs);

                while (_durations.Count > MaxSamples)
                    _durations.TryDequeue(out _);
            }

            public string GetStats(string name)
            {
                var events = _eventTicks.ToArray();
                var durations = _durations.ToArray();

                if (events.Length == 0 && durations.Length == 0)
                    return $"{name}: No data";

                string stats = $"{name}:";

                // Event rate
                if (events.Length > 1)
                {
                    var elapsed = TimeSpan.FromTicks(events[events.Length - 1] - events[0]);
                    if (elapsed.TotalSeconds > 0)
                    {
                        var rate = events.Length / elapsed.TotalSeconds;
                        stats += $" Rate={rate:F1}/s";
                    }
                }

                // Duration stats
                if (durations.Length > 0)
                {
                    var avg = durations.Average();
                    var max = durations.Max();
                    stats += $" Avg={avg:F1}ms Max={max}ms";
                }

                return stats;
            }
        }

        private class TimingScope : IDisposable
        {
            private readonly string _name;
            private readonly Stopwatch _sw;

            public TimingScope(string name)
            {
                _name = name;
                _sw = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _sw.Stop();
                var metric = _metrics.GetOrAdd(_name, _ => new MetricData());
                metric.RecordDuration(_sw.ElapsedMilliseconds);
            }
        }
    }
}