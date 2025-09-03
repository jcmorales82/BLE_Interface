using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;

namespace BLE_Interface.Services.Bluetooth
{
    /// <summary>
    /// Thin wrapper around BluetoothLEAdvertisementWatcher with safe Start/Stop.
    /// Defaults to Tyme Wear service filter. Includes a watchdog that restarts
    /// the watcher if no packets arrive for several seconds (prevents “stuck” RSSI).
    /// </summary>
    public sealed class BleScanner : IDisposable
    {
        private BluetoothLEAdvertisementWatcher _watcher;
        private TaskCompletionSource<bool> _stoppedTcs;
        private Guid _filterUuid = BleConstants.TymewearServiceUuid;

        // Watchdog
        private DateTimeOffset _lastReceived = DateTimeOffset.MinValue;
        private Timer _watchdog;

        public event Action<ScanResult> DeviceFound;

        public bool IsRunning => _watcher != null &&
                                 _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

        public async Task StartAsync(Guid? filterServiceUuid = null, CancellationToken ct = default)
        {
            await StopAsync().ConfigureAwait(false); // ensure a clean start

            _filterUuid = filterServiceUuid ?? BleConstants.TymewearServiceUuid;
            _lastReceived = DateTimeOffset.UtcNow;

            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            // Allow extended advertisements when available (older OS may throw)
            try { _watcher.AllowExtendedAdvertisements = true; } catch { /* ignore on older builds */ }

            // Service UUID filter
            var filter = new BluetoothLEAdvertisementFilter();
            filter.Advertisement.ServiceUuids.Add(_filterUuid);
            _watcher.AdvertisementFilter = filter;

            _watcher.Received += OnReceived;
            _watcher.Stopped += OnStopped;

            _stoppedTcs = new TaskCompletionSource<bool>();
            _watcher.Start();

            // Start watchdog: check every 5s; if >8s silent, restart
            _watchdog = new Timer(_ => { WatchdogTick(); }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public async Task StopAsync()
        {
            if (_watchdog != null) { try { _watchdog.Dispose(); } catch { } _watchdog = null; }

            if (_watcher == null) return;

            try { _watcher.Stop(); } catch { /* ignore */ }

            var tcs = _stoppedTcs;
            _stoppedTcs = null;

            _watcher.Received -= OnReceived;
            _watcher.Stopped -= OnStopped;

            if (tcs != null)
            {
                await Task.WhenAny(tcs.Task, Task.Delay(500)).ConfigureAwait(false);
            }

            _watcher = null;
        }

        private void WatchdogTick()
        {
            // If we haven't seen a packet in a while, restart the watcher.
            var silentFor = DateTimeOffset.UtcNow - _lastReceived;
            if (IsRunning && silentFor > TimeSpan.FromSeconds(8))
            {
                // Restart on a background task to avoid blocking the timer thread.
                Task.Run(async () =>
                {
                    try
                    {
                        await StopAsync().ConfigureAwait(false);
                        await StartAsync(_filterUuid).ConfigureAwait(false);
                    }
                    catch { /* ignore; next tick will try again */ }
                });
            }
        }

        private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            _lastReceived = DateTimeOffset.UtcNow;

            var name = "Unknown"; // name is not used now (UI shows TYME-XXXX)
            var rssi = args.RawSignalStrengthInDBm;
            var ts = args.Timestamp; // DateTimeOffset

            var result = new ScanResult(args.BluetoothAddress, name, rssi, ts);
            try { DeviceFound?.Invoke(result); } catch { /* don't let UI exceptions kill scanning */ }
        }

        private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            try { _stoppedTcs?.TrySetResult(true); } catch { }
        }

        public void Dispose() => _ = StopAsync();
    }

    public sealed class ScanResult
    {
        public ulong Address { get; }
        public string Name { get; }   // not used by TYME-XXXX display, kept for completeness
        public short Rssi { get; }
        public DateTimeOffset Timestamp { get; }

        public ScanResult(ulong address, string name, short rssi, DateTimeOffset timestamp)
        {
            Address = address;
            Name = string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
            Rssi = rssi;
            Timestamp = timestamp;
        }

        public string AddressHex => Address.ToString("X");
        public override string ToString() => $"{Name} [{AddressHex}] RSSI={Rssi} @ {Timestamp:t}";
    }
}
