using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;   // INotifyPropertyChanged
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using BLE_Interface.Services.Bluetooth;

namespace BLE_Interface.Views
{
    public partial class ScanTestWindow : Window
    {
        private readonly BleScanner _scanner = new BleScanner();

        // Address -> row in the grid
        private readonly Dictionary<ulong, Row> _rows = new Dictionary<ulong, Row>();

        // Latest raw readings (we update UI on a steady cadence)
        private readonly Dictionary<ulong, Telemetry> _latest = new Dictionary<ulong, Telemetry>();

        // 4 Hz UI updater (feels responsive without thrash)
        private readonly DispatcherTimer _updateTimer;

        // Simple single active connection for this test window
        private BluetoothLEDevice _device;
        private GattDeviceService _batteryService;
        private GattCharacteristic _batteryLevelChar;
        private Row _connectedRow;

        public ObservableCollection<Row> Items { get; } = new ObservableCollection<Row>();

        public ScanTestWindow()
        {
            InitializeComponent();
            Grid.ItemsSource = Items;

            _scanner.DeviceFound += OnDeviceFound;

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _updateTimer.Tick += (s, e) => RefreshRowsFromLatest();
            _updateTimer.Start();
        }

        // ---- Scanning ----
        private void OnDeviceFound(ScanResult r)
        {
            // Ignore bogus RSSI sentinel values (Windows sometimes reports -127)
            if (r.Rssi <= -120) return;

            // Update "latest" snapshot for this device
            var tele = new Telemetry { Rssi = r.Rssi, LastSeen = r.Timestamp };
            _latest[r.Address] = tele;

            // Ensure a row exists
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_rows.ContainsKey(r.Address)) return;

                var row = new Row
                {
                    Address = r.Address,
                    Name = TymeName(r.Address),
                    Rssi = r.Rssi,
                    LastSeen = r.Timestamp,
                    _rssiEma = r.Rssi,
                    _hasRssi = true
                };

                _rows[r.Address] = row;
                Items.Add(row);

                // Bound the grid to avoid unbounded growth
                const int capacity = 500;
                if (Items.Count > capacity) Items.RemoveAt(0);
            }));
        }

        private static string TymeName(ulong address)
        {
            var last4 = (address & 0xFFFFUL);
            return $"TYME-{last4:X4}";
        }

        private void RefreshRowsFromLatest()
        {
            foreach (var kv in _rows)
            {
                var addr = kv.Key;
                var row = kv.Value;

                Telemetry t;
                if (_latest.TryGetValue(addr, out t))
                {
                    // Ignore bogus sentinel values (-127 etc.)
                    if (t.Rssi > -120)
                    {
                        // Light exponential smoothing
                        if (row._hasRssi) row._rssiEma = 0.6 * row._rssiEma + 0.4 * t.Rssi;
                        else { row._rssiEma = t.Rssi; row._hasRssi = true; }

                        var rounded = (short)Math.Round(row._rssiEma);
                        if (rounded != row.Rssi) row.Rssi = rounded;
                        if (t.LastSeen != row.LastSeen) row.LastSeen = t.LastSeen;
                    }
                }
            }
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            Items.Clear();
            _rows.Clear();
            _latest.Clear();
            await _scanner.StartAsync(BleConstants.TymewearServiceUuid); // Tymewear-only
        }

        private async void StopScan_Click(object sender, RoutedEventArgs e)
        {
            await _scanner.StopAsync();
        }

        // ---- Connect / Battery ----
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as Row;
            if (row == null) return;

            await ConnectToAsync(row);
        }

        private async void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            await DisconnectAsync();
        }

        private async System.Threading.Tasks.Task ConnectToAsync(Row row)
        {
            // Disconnect any previous
            await DisconnectAsync();

            try
            {
                // Connect by Bluetooth address
                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(row.Address);
                if (_device == null)
                {
                    MessageBox.Show("Failed to connect to device.", "Connect", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get Battery service
                var svcResult = await _device.GetGattServicesForUuidAsync(BleConstants.BatteryServiceUuid, BluetoothCacheMode.Uncached);
                if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                {
                    // Try cached as fallback
                    svcResult = await _device.GetGattServicesForUuidAsync(BleConstants.BatteryServiceUuid, BluetoothCacheMode.Cached);
                }

                if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                {
                    // No battery service
                    row.BatteryPercent = null;
                    _connectedRow = row;
                    return;
                }

                _batteryService = svcResult.Services[0];

                // Get Battery Level characteristic
                var chResult = await _batteryService.GetCharacteristicsForUuidAsync(BleConstants.BatteryLevelCharacteristicUuid, BluetoothCacheMode.Uncached);
                if (chResult.Status != GattCommunicationStatus.Success || chResult.Characteristics.Count == 0)
                {
                    // Try cached fallback
                    chResult = await _batteryService.GetCharacteristicsForUuidAsync(BleConstants.BatteryLevelCharacteristicUuid, BluetoothCacheMode.Cached);
                }

                if (chResult.Status != GattCommunicationStatus.Success || chResult.Characteristics.Count == 0)
                {
                    row.BatteryPercent = null;
                    _connectedRow = row;
                    return;
                }

                _batteryLevelChar = chResult.Characteristics[0];
                _connectedRow = row;

                // Read once
                await ReadBatteryAsync();

                // Subscribe to notifications if supported
                if ((_batteryLevelChar.CharacteristicProperties & (GattCharacteristicProperties.Notify | GattCharacteristicProperties.Indicate)) != 0)
                {
                    _batteryLevelChar.ValueChanged += Battery_ValueChanged;
                    try
                    {
                        await _batteryLevelChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    }
                    catch
                    {
                        // Some devices require Indicate or don't support CCCD writes; ignore if it fails.
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connect error: " + ex.Message, "Connect", MessageBoxButton.OK, MessageBoxImage.Error);
                await DisconnectAsync();
            }
        }

        private async System.Threading.Tasks.Task ReadBatteryAsync()
        {
            if (_batteryLevelChar == null || _connectedRow == null) return;

            try
            {
                var read = await _batteryLevelChar.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (read.Status == GattCommunicationStatus.Success)
                {
                    var percent = ParseBattery(read.Value);
                    _connectedRow.BatteryPercent = percent;
                }
            }
            catch
            {
                // ignore read errors
            }
        }

        private void Battery_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var value = ParseBattery(args.CharacteristicValue);
            if (_connectedRow != null && value.HasValue)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _connectedRow.BatteryPercent = value;
                }));
            }
        }

        private static int? ParseBattery(IBuffer buffer)
        {
            try
            {
                using (var reader = DataReader.FromBuffer(buffer))
                {
                    byte b = reader.ReadByte();
                    int val = b;           // 0..255
                    if (val > 100) val = 100;
                    // no need to check below 0 for a byte
                    return val;
                }
            }
            catch { return null; }
        }


        private async System.Threading.Tasks.Task DisconnectAsync()
        {
            try
            {
                if (_batteryLevelChar != null)
                {
                    try
                    {
                        await _batteryLevelChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    }
                    catch { /* ignore */ }
                    try { _batteryLevelChar.ValueChanged -= Battery_ValueChanged; } catch { }
                }
            }
            catch { /* ignore */ }

            _batteryLevelChar = null;

            try { _batteryService?.Dispose(); } catch { }
            _batteryService = null;

            try { _device?.Dispose(); } catch { }
            _device = null;

            _connectedRow = null;
        }

        protected override async void OnClosed(EventArgs e)
        {
            _updateTimer.Stop();
            await _scanner.StopAsync();
            await DisconnectAsync();
            base.OnClosed(e);
        }

        private struct Telemetry
        {
            public short Rssi;
            public DateTimeOffset LastSeen;
        }

        public sealed class Row : INotifyPropertyChanged
        {
            // EMA state
            internal double _rssiEma;
            internal bool _hasRssi;

            private string _name;
            private short _rssi;
            private DateTimeOffset _lastSeen;
            private int? _battery;

            public ulong Address { get; set; }

            public string Name
            {
                get => _name;
                set { if (_name != value) { _name = value; OnPropertyChanged(); } }
            }

            public short Rssi
            {
                get => _rssi;
                set { if (_rssi != value) { _rssi = value; OnPropertyChanged(); } }
            }

            public DateTimeOffset LastSeen
            {
                get => _lastSeen;
                set { if (_lastSeen != value) { _lastSeen = value; OnPropertyChanged(); } }
            }

            public int? BatteryPercent
            {
                get => _battery;
                set { if (_battery != value) { _battery = value; OnPropertyChanged(); } }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
