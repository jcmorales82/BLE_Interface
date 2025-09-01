using BLE_Interface.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BLE_Interface.Services
{
    /// <summary>
    /// Service handling BLE scanning, connection, command/response, streaming, and file downloads.
    /// </summary>
    public class BleService : IDisposable
    {
        // ===== Battery helpers (inline so you can compile without extra files) =====
        // Standard GATT Battery Service (0x180F / 0x2A19) session
        private sealed class BatteryServiceSession : IDisposable
        {
            private GattCharacteristic _battChar;

            public event Action<byte> BatteryLevelChanged;

            private BatteryServiceSession(GattCharacteristic ch)
            {
                _battChar = ch;
            }

            public static async Task<BatteryServiceSession> CreateAsync(BluetoothLEDevice dev, Action<byte> onLevel = null)
            {
                var svcResult = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
                if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                    return null;

                var svc = svcResult.Services[0];
                var chrResult = await svc.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
                if (chrResult.Status != GattCommunicationStatus.Success || chrResult.Characteristics.Count == 0)
                    return null;

                var ch = chrResult.Characteristics[0];
                var session = new BatteryServiceSession(ch);
                if (onLevel != null) session.BatteryLevelChanged += onLevel;

                await session.InitializeAsync();
                return session;
            }

            private async Task InitializeAsync()
            {
                try
                {
                    _battChar.ValueChanged += OnValueChanged;
                    await _battChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    var val = await ReadAsync();
                    if (val.HasValue) BatteryLevelChanged?.Invoke(val.Value);
                }
                catch
                {
                    // ignore if notify not supported
                }
            }

            private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
            {
                try
                {
                    var dr = DataReader.FromBuffer(args.CharacteristicValue);
                    byte level = dr.ReadByte();
                    BatteryLevelChanged?.Invoke(level);
                }
                catch { /* ignore parse errors */ }
            }

            public async Task<byte?> ReadAsync()
            {
                try
                {
                    var read = await _battChar.ReadValueAsync();
                    if (read.Status == GattCommunicationStatus.Success)
                    {
                        var dr = DataReader.FromBuffer(read.Value);
                        return dr.ReadByte();
                    }
                }
                catch { /* ignore */ }
                return null;
            }

            public void Dispose()
            {
                if (_battChar != null)
                {
                    try { _battChar.ValueChanged -= OnValueChanged; } catch { }
                    _battChar = null;
                }
            }
        }

        // Simple logger that writes to the same "Downloads" folder you already use
        private sealed class BatteryLogger
        {
            private readonly string _filePath;
            private readonly object _lock = new object();
            public string FilePath => _filePath;

            public BatteryLogger(string deviceDisplayName)
            {
                Directory.CreateDirectory("Downloads");
                var safeName = Sanitize(deviceDisplayName);
                var fileName = $"{DateTime.Now:MM_dd_yy_HH_mm}_{safeName}_BatteryProfile.csv";
                _filePath = Path.Combine("Downloads", fileName);

                if (!File.Exists(_filePath))
                {
                    File.WriteAllText(_filePath, "Timestamp,Charge,Battery%\r\n", Encoding.UTF8);
                }
            }

            public void Append(string timestamp, int charge, int? batteryPercent)
            {
                var bp = batteryPercent.HasValue ? batteryPercent.Value.ToString() : "";
                lock (_lock)
                {
                    File.AppendAllText(_filePath, $"{timestamp},{charge},{bp}\r\n", Encoding.UTF8);
                }
            }

            private static string Sanitize(string name)
            {
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');
                return name;
            }
        }
        // ===== End battery helpers =====


        // Helper types for extended data
        public class ExtendedDataPoint
        {
            public uint TimestampRel { get; set; }
            public ushort Chest { get; set; }
            public ushort ChestNormalized { get; set; }
            public List<IMUSample> IMU { get; set; }
            public List<ushort> PlayerLoad { get; set; }
            public uint Pressure { get; set; }
            public short Temperature { get; set; }
        }

        // BLE scanning
        public ObservableCollection<BleDeviceModel> DiscoveredDevices { get; } = new ObservableCollection<BleDeviceModel>();
        private readonly Dictionary<ulong, BleDeviceModel> _deviceMap = new Dictionary<ulong, BleDeviceModel>();
        private readonly BluetoothLEAdvertisementWatcher _watcher;

        // Connected device and characteristics
        private BluetoothLEDevice _connectedDevice;
        private BleDeviceModel _connectedDeviceModel; // <— to update Battery% on the list
        public ulong DeviceAddress { get; private set; }
        public GattCharacteristic ControlCharacteristic { get; private set; }
        public GattCharacteristic DataStreamCharacteristic { get; private set; }
        public GattCharacteristic DownloadCharacteristic { get; private set; }

        // Command/response tracking
        private ushort nextTagId = 1;
        private readonly Dictionary<ushort, TaskCompletionSource<byte[]>> pendingResponses = new Dictionary<ushort, TaskCompletionSource<byte[]>>();
        private readonly Dictionary<ushort, ushort> tagToOpcode = new Dictionary<ushort, ushort>();

        // File download
        private readonly List<FileEntry> fileList = new List<FileEntry>();
        public IReadOnlyList<FileEntry> FileList => fileList;
        private CancellationTokenSource _fileDownloadCts;
        private readonly List<byte> _streamingBuffer = new List<byte>();
        private StreamWriter decodedWriter;
        private bool _wroteBreathHeader;
        private bool _wroteImuHeader;
        private bool _wroteHrHeader;

        // Stretch streaming
        private FileStream stretchFileStream;
        private ushort latestStretchValue;

        // Logging and events
        public event Action<string> LogMessage;
        public event Action<string> DataStreamUpdated;
        public event Action FileDownloadComplete;
        public event Action DeviceConnected;
        public event Action<uint, ushort> StretchDataReceived;
        public event Action<ExtendedDataPoint> ExtendedDataReceived;
        public event Action<List<IMUSample>> IMUSamplesReceived;

        // Battery
        private BatteryServiceSession _batterySession;
        private BatteryLogger _batteryLogger;
        public event Action<int> BatteryLevelUpdated;

        // Track start time of first battery sample for hh:mm elapsed
        private DateTime? _batteryStartLocalTime;

        public ushort LatestStretchValue => latestStretchValue;

        // Opcodes and response codes
        private readonly Dictionary<ushort, string> opcodeNames = new Dictionary<ushort, string>
        {
            {0x0001, "GET_INFO"}, {0x0020, "START_ACTIVITY"},
            {0x0021, "STOP_ACTIVITY"}, {0x002B, "ERASE_FILES"},
            {0x002C, "SYNC_RTC"}, {0x002D, "START_STRETCH"},
            {0x002E, "STOP_STRETCH"}, {0x0108, "DATA_DUMP"},
            {0x0109, "LIST_FILES"}, {0x010A, "FACTORY_CALIBRATION"}
        };
        private readonly Dictionary<ushort, string> responseCodes = new Dictionary<ushort, string>
        {
            {0x8000, "SUCCESS"}, {0x8100, "INVALID_REQ"}, {0x8200, "INVALID_PARAM"},
            {0x8300, "NOT_FOUND"}, {0x8400, "ERROR"}, {0x8500, "BUSY"},
            {0x8600, "LOCKED"}, {0x8700, "FORBIDDEN"}, {0x8800, "NO_MEM"},
            {0x4000, "STAT_NO_MEM"}, {0x4001, "STAT_FITTING"}
        };

        public BleService()
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Stopped += (s, e) => Log("Scan stopped.");
        }

        public void StartScanning()
        {
            if (_watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _deviceMap.Clear();
                DiscoveredDevices.Clear();
                _watcher.Start();
                Log("🔍 BLE scan started");
            }
        }

        public void StopScanning()
        {
            _watcher.Stop();
            Log("Stopped scanning for BLE devices.");
        }

        public async Task<byte[]> FactoryCalibrationAsync()
        {
            return await SendControlCommandAsync(0x010A);
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var svcUuid = new Guid("40B50000-30B5-11E5-A151-FEFF819CDC90");
            if (!args.Advertisement.ServiceUuids.Contains(svcUuid)) return;

            ulong address = args.BluetoothAddress;
            string hex = address.ToString("X").PadLeft(12, '0');
            string shortId = hex.Substring(hex.Length - 4);
            string name = $"TYME-{shortId}";
            int rssi = args.RawSignalStrengthInDBm;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_deviceMap.ContainsKey(address))
                {
                    var dev = new BleDeviceModel { Address = hex, Name = name, Rssi = rssi };
                    _deviceMap[address] = dev;
                    DiscoveredDevices.Add(dev);
                    Log($"Discovered device: {name} (RSSI {rssi})");
                }
                else
                {
                    _deviceMap[address].Rssi = rssi;
                }
            }));
        }

        public async Task<bool> ConnectToDeviceAsync(BleDeviceModel deviceModel)
        {
            // 1) Stop scanning
            StopScanning();

            // 2) Tear down any existing connection
            if (_connectedDevice != null)
            {
                if (ControlCharacteristic != null)
                {
                    await ControlCharacteristic
                        .WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    ControlCharacteristic.ValueChanged -= ControlCharacteristic_ValueChanged;
                }
                if (DataStreamCharacteristic != null)
                {
                    await DataStreamCharacteristic
                        .WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    DataStreamCharacteristic.ValueChanged -= DataStreamCharacteristic_ValueChanged;
                }
                if (DownloadCharacteristic != null)
                {
                    await DownloadCharacteristic
                        .WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    DownloadCharacteristic.ValueChanged -= DownloadCharacteristic_ValueChanged;
                }

                _batterySession?.Dispose();
                _batterySession = null;
                _connectedDevice.Dispose();
                _connectedDevice = null;
            }

            // 3) Parse the address string into a ulong
            ulong addr = Convert.ToUInt64(deviceModel.Address, 16);

            // 4) Connect to the BLE device
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(addr).AsTask();
            if (device == null)
            {
                Log("❌ Could not connect to device.");
                return false;
            }
            _connectedDevice = device;
            _connectedDeviceModel = deviceModel;

            // 5) Store the address so we can tag events later
            DeviceAddress = addr;

            // 6) Small delay
            await Task.Delay(200);

            // 7) Discover your custom service (with a few retries)
            var svcUuid = new Guid("40B50000-30B5-11E5-A151-FEFF819CDC90");
            GattDeviceServicesResult svcRes = null;
            for (int i = 0; i < 3; i++)
            {
                svcRes = await _connectedDevice.GetGattServicesForUuidAsync(
                    svcUuid, BluetoothCacheMode.Uncached);
                if (svcRes.Status == GattCommunicationStatus.Success &&
                    svcRes.Services.Count > 0)
                {
                    break;
                }
                await Task.Delay(200);
            }
            if (svcRes?.Status != GattCommunicationStatus.Success ||
                svcRes.Services.Count == 0)
            {
                Log("❌ Custom BLE service not found.");
                return false;
            }
            var service = svcRes.Services[0];

            // 8) Fetch each characteristic
            DataStreamCharacteristic = await FetchCharAsync(service,
                new Guid("40B50004-30B5-11E5-A151-FEFF819CDC90"),
                DataStreamCharacteristic_ValueChanged);

            ControlCharacteristic = await FetchCharAsync(service,
                new Guid("40B50007-30B5-11E5-A151-FEFF819CDC90"),
                ControlCharacteristic_ValueChanged);

            DownloadCharacteristic = await FetchCharAsync(service,
                new Guid("40B50001-30B5-11E5-A151-FEFF819CDC90"),
                DownloadCharacteristic_ValueChanged);

            // 9) Notify UI
            DeviceConnected?.Invoke();
            Log($"✅ Connected to {deviceModel.Name} (Addr: {DeviceAddress:X}).");

            // 10) Battery: create logger and attach to 0x180F/0x2A19
            var displayName = _connectedDeviceModel?.Name ?? _connectedDevice?.Name ?? "TYME-XXXX";
            _batteryLogger = new BatteryLogger(displayName);

            _batterySession = await BatteryServiceSession.CreateAsync(_connectedDevice, level =>
            {
                try
                {
                    BatteryLevelUpdated?.Invoke(level);
                    if (_connectedDeviceModel != null) _connectedDeviceModel.BatteryPercent = level;
                }
                catch { /* ignore UI update errors */ }
            });

            // Reset elapsed timer for CSV on new connection
            _batteryStartLocalTime = null;

            return true;
        }

        private async Task<GattCharacteristic> FetchCharAsync(GattDeviceService service, Guid charUuid, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler)
        {
            var res = await service.GetCharacteristicsForUuidAsync(charUuid);
            if (res.Status != GattCommunicationStatus.Success || res.Characteristics.Count == 0)
            {
                Log($"❌ Characteristic {charUuid} not found."); return null;
            }
            var c = res.Characteristics[0];
            c.ValueChanged += handler;
            await EnableNotifyAsync(c);
            return c;
        }

        private async Task EnableNotifyAsync(GattCharacteristic c)
        {
            var status = await c.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                await Task.Delay(100);
                await c.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            }
        }

        // Made async so we can await a 0x2A19 read when 0x4002 charge arrives
        private async void ControlCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            reader.ByteOrder = ByteOrder.LittleEndian;

            // 1) Read the 16 bit “message type” (resp code) and 16 bit “tag” (for status, this is the counter)
            ushort respCode = reader.ReadUInt16();
            ushort tagOrCounter = reader.ReadUInt16();

            // 2) Copy the remaining bytes into payload[]
            var payload = new byte[args.CharacteristicValue.Length - 4];
            reader.ReadBytes(payload);

            // 3) Status 0x4002 = battery charge sample
            if (respCode == 0x4002)
            {
                if (payload.Length >= 4)
                {
                    // Firmware-provided raw charge value
                    uint chargeValue = BitConverter.ToUInt32(payload, 0);

                    // Read standard Battery Level (0–100%) right after receiving the charge
                    int? battPct = null;
                    if (_batterySession != null)
                    {
                        try
                        {
                            var v = await _batterySession.ReadAsync();
                            if (v.HasValue)
                            {
                                battPct = v.Value;
                                BatteryLevelUpdated?.Invoke(v.Value);
                                if (_connectedDeviceModel != null) _connectedDeviceModel.BatteryPercent = v.Value;
                            }
                        }
                        catch { /* ignore read errors */ }
                    }

                    // Use PC clock time; compute elapsed from first sample for the CSV
                    var now = DateTime.Now;
                    if (!_batteryStartLocalTime.HasValue) _batteryStartLocalTime = now;
                    var elapsed = now - _batteryStartLocalTime.Value;

                    // Use TOTAL minutes (rounded) so 59–61s jitter still maps to 1 min
                    int totalMinutes = (int)Math.Round(elapsed.TotalMinutes, MidpointRounding.AwayFromZero);
                    string hhmm = $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";

                    // CSV row: elapsed hh:mm, raw charge, battery %
                    try
                    {
                        _batteryLogger?.Append(hhmm, (int)chargeValue, battPct);
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Failed to write battery CSV: {ex.Message}");
                    }

                    // Log line: only Charge and Battery%
                    Log($"🔋 Battery Status → Charge: {chargeValue}, Battery: {(battPct.HasValue ? battPct.Value.ToString() : "—")}%");
                }
                else
                {
                    Log($"⚠️ Battery Status received but payload too short (length={payload.Length})");
                }

                return; // handled
            }

            // 4) Otherwise, handle as a regular response
            string codeName = responseCodes.TryGetValue(respCode, out var rn) ? rn : "UNKNOWN";
            if (tagToOpcode.TryGetValue(tagOrCounter, out var sentOpcode))
            {
                string cmdName = opcodeNames.TryGetValue(sentOpcode, out var cn) ? cn : $"0x{sentOpcode:X4}";
                Log($"Response to {cmdName}: {codeName}");
            }
            else
            {
                Log($"Response: {codeName}");
            }

            // 5) Complete any pending TaskCompletionSource for this tag
            if (pendingResponses.TryGetValue(tagOrCounter, out var tcs))
            {
                pendingResponses.Remove(tagOrCounter);
                tagToOpcode.Remove(tagOrCounter);
                tcs.SetResult(payload);
            }
        }

        private void DataStreamCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // Copy the raw bytes from the BLE notification
            var buffer = args.CharacteristicValue;
            byte[] raw = new byte[buffer.Length];
            DataReader.FromBuffer(buffer).ReadBytes(raw);
            if (raw.Length < 1) return;

            byte type = raw[0];
            try
            {
                switch (type)
                {
                    case 0: // Extended mode datapoint
                        if (raw.Length < 85) return;

                        uint counter = BitConverter.ToUInt32(raw, 1);
                        ushort chestRaw = BitConverter.ToUInt16(raw, 5);
                        ushort chestNorm = BitConverter.ToUInt16(raw, 7);

                        var imuList = new List<IMUSample>(5);
                        int offset = 9;
                        for (int i = 0; i < 5; i++)
                        {
                            var sample = new IMUSample
                            {
                                AccX = BitConverter.ToInt16(raw, offset + 0),
                                AccY = BitConverter.ToInt16(raw, offset + 2),
                                AccZ = BitConverter.ToInt16(raw, offset + 4),
                                GyrX = BitConverter.ToInt16(raw, offset + 6),
                                GyrY = BitConverter.ToInt16(raw, offset + 8),
                                GyrZ = BitConverter.ToInt16(raw, offset + 10)
                            };
                            imuList.Add(sample);
                            offset += 12;
                        }

                        IMUSamplesReceived?.Invoke(imuList);

                        var loadList = new List<ushort>(5);
                        for (int i = 0; i < 5; i++)
                        {
                            loadList.Add(BitConverter.ToUInt16(raw, offset));
                            offset += 2;
                        }

                        uint pressure = BitConverter.ToUInt32(raw, offset);
                        short temperature = BitConverter.ToInt16(raw, offset + 4);

                        var extPoint = new ExtendedDataPoint
                        {
                            TimestampRel = counter,
                            Chest = chestRaw,
                            ChestNormalized = chestNorm,
                            IMU = imuList,
                            PlayerLoad = loadList,
                            Pressure = pressure,
                            Temperature = temperature
                        };
                        Log($"Pressure={pressure}, Temp={temperature}");

                        ExtendedDataReceived?.Invoke(extPoint);
                        break;

                    case 1: // Breathing (processed) data
                        if (raw.Length < 17) return;
                        uint breathTs = BitConverter.ToUInt32(raw, 1);
                        ushort rawBR = BitConverter.ToUInt16(raw, 5);
                        ushort procBR = BitConverter.ToUInt16(raw, 7);
                        ushort rawTV = BitConverter.ToUInt16(raw, 9);
                        ushort procTV = BitConverter.ToUInt16(raw, 11);
                        ushort rawMV = BitConverter.ToUInt16(raw, 13);
                        ushort procMV = BitConverter.ToUInt16(raw, 15);
                        Log($"🌬️ Breathing – Timestamp={breathTs}, rawBR={rawBR}, procBR={procBR}, rawTV={rawTV}, procTV={procTV}, rawMV={rawMV}, procMV={procMV}");
                        break;

                    case 2: // IMU Processed data
                        if (raw.Length < 13) return;
                        uint imuCounter = BitConverter.ToUInt32(raw, 1);
                        ushort cadenceVal = BitConverter.ToUInt16(raw, 5);
                        uint stepTime = BitConverter.ToUInt32(raw, 7);
                        ushort playerLd = BitConverter.ToUInt16(raw, 11);
                        Log($"🤖 IMU – Counter={imuCounter}, Cadence={cadenceVal}, StepTime={stepTime}, PlayerLoad={playerLd}");
                        break;

                    case 3: // Stretch data
                        if (raw.Length < 7) return;
                        uint stretchCounter = BitConverter.ToUInt32(raw, 1);
                        ushort stretchVal = BitConverter.ToUInt16(raw, 5);
                        Log($"🏋️ Stretch – Counter={stretchCounter}, Value={stretchVal}");
                        if (stretchFileStream != null)
                        {
                            string line = $"{stretchCounter},{stretchVal}\n";
                            byte[] bytes = Encoding.UTF8.GetBytes(line);
                            stretchFileStream.Write(bytes, 0, bytes.Length);
                        }
                        break;

                    case 4: // Pressure & Temperature data
                        if (raw.Length < 11) return;
                        uint ptCounter = BitConverter.ToUInt32(raw, 1);
                        uint ptPressure = BitConverter.ToUInt32(raw, 5);
                        short ptTemp = BitConverter.ToInt16(raw, 9);
                        Log($"🌡️ Pressure/Temp – Counter={ptCounter}, Pressure={ptPressure}, Temperature={ptTemp}");
                        break;

                    case 5: // Heart Rate data
                        if (raw.Length < 7) return;
                        uint hrTs = BitConverter.ToUInt32(raw, 1);
                        ushort hrVal = BitConverter.ToUInt16(raw, 5);
                        Log($"❤️‍🩹 HR – Timestamp={hrTs}, HR={hrVal}");
                        break;

                    default:
                        Log($"⚠️ Unknown dataType 0x{type:X2}, ignoring packet.");
                        break;
                }

                DataStreamUpdated?.Invoke("Updated");
            }
            catch (Exception ex)
            {
                Log($"⚠️ Exception while parsing dataType 0x{type:X2}: {ex.Message}");
            }
        }

        private void DownloadCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (decodedWriter == null) return;
            _fileDownloadCts?.Cancel();
            _fileDownloadCts = new CancellationTokenSource();
            StartInactivityMonitor(_fileDownloadCts.Token);

            var incoming = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(incoming);
            _streamingBuffer.AddRange(incoming);

            int idx = 0;
            while (idx < _streamingBuffer.Count)
            {
                byte t = _streamingBuffer[idx];
                int len = t switch { 1 => 16, 2 => 12, 5 => 6, _ => -1 };
                if (len < 0) { _streamingBuffer.Clear(); return; }
                if (idx + 1 + len > _streamingBuffer.Count) break;

                var pl = _streamingBuffer.Skip(idx + 1).Take(len).ToArray();
                idx += 1 + len;

                switch (t)
                {
                    case 1:
                        if (!_wroteBreathHeader) { decodedWriter.WriteLine("[Breathing]"); _wroteBreathHeader = true; }
                        decodedWriter.WriteLine($"{BitConverter.ToUInt32(pl, 0)},{BitConverter.ToUInt16(pl, 4)},{BitConverter.ToUInt16(pl, 6)},{BitConverter.ToUInt16(pl, 8)},{BitConverter.ToUInt16(pl, 10)},{BitConverter.ToUInt16(pl, 12)},{BitConverter.ToUInt16(pl, 14)}");
                        break;
                    case 2:
                        if (!_wroteImuHeader) { decodedWriter.WriteLine("[IMU]"); _wroteImuHeader = true; }
                        decodedWriter.WriteLine($"{BitConverter.ToUInt32(pl, 0)},{BitConverter.ToUInt16(pl, 4)},{BitConverter.ToUInt32(pl, 6)},{BitConverter.ToUInt16(pl, 10)}");
                        break;
                    case 5:
                        if (!_wroteHrHeader) { decodedWriter.WriteLine("[HR]"); _wroteHrHeader = true; }
                        decodedWriter.WriteLine($"{BitConverter.ToUInt32(pl, 0)},{BitConverter.ToUInt16(pl, 4)}");
                        break;
                }
            }
            if (idx > 0) _streamingBuffer.RemoveRange(0, idx);
        }

        private void StartInactivityMonitor(CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, token);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Log("✅ File download complete.");
                        decodedWriter?.Close(); decodedWriter = null;
                    });
                }
                catch (TaskCanceledException) { }
            });
        }

        public async Task<byte[]> SendControlCommandAsync(ushort opcode, uint? param = null)
        {
            if (ControlCharacteristic == null)
                throw new InvalidOperationException("ControlCharacteristic is not available (not connected).");

            ushort tag = nextTagId++;
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(opcode);
                w.Write(tag);
                if (param.HasValue) w.Write(param.Value);
            }
            var cmd = ms.ToArray();
            var tcs = new TaskCompletionSource<byte[]>();
            pendingResponses[tag] = tcs;
            tagToOpcode[tag] = opcode;
            await ControlCharacteristic.WriteValueAsync(cmd.AsBuffer());
            Log($"Sent 0x{opcode:X4} tag {tag}");
            return await tcs.Task;
        }

        public async Task SyncRTCAsync() => await SendControlCommandAsync(0x002C, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public async Task StartActivityAsync() => await SendControlCommandAsync(0x0020);
        public async Task StopActivityAsync() => await SendControlCommandAsync(0x0021);

        public async Task ListFilesAsync()
        {
            var data = await SendControlCommandAsync(0x0109);
            fileList.Clear();
            int sz = Marshal.SizeOf<FileEntry>();
            for (int j = 0; j < data.Length; j += sz)
                fileList.Add(ByteArrayToStructure<FileEntry>(data.Skip(j).Take(sz).ToArray()));
            Log($"File list: {fileList.Count}");
        }

        public async Task DownloadFileAsync(FileEntry file)
        {
            Directory.CreateDirectory("Downloads");
            string bn = file.GetFilename();
            string path = Path.Combine("Downloads", Path.ChangeExtension(bn, ".txt"));
            int c = 1;
            while (File.Exists(path)) path = Path.Combine("Downloads", $"{Path.GetFileNameWithoutExtension(bn)}_{c++}.txt");

            decodedWriter = new StreamWriter(path, false, Encoding.UTF8);
            _streamingBuffer.Clear();
            _wroteBreathHeader = _wroteImuHeader = _wroteHrHeader = false;
            _fileDownloadCts?.Cancel();
            _fileDownloadCts = new CancellationTokenSource();
            StartInactivityMonitor(_fileDownloadCts.Token);

            Log($"Downloading file [{file.timestamp}]");
            await SendControlCommandAsync(0x0108, file.timestamp);
            while (decodedWriter != null) await Task.Delay(100);
        }

        public async Task StartStretchStreamAsync(string deviceName)
        {
            if (_connectedDevice == null || ControlCharacteristic == null)
            {
                Log("Not connected"); return;
            }
            Directory.CreateDirectory("Downloads");
            uint epoch = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string safe = deviceName.Replace(':', '_').Replace(' ', '_');
            string fn = $"{safe}_{epoch}_raw.txt";
            stretchFileStream = File.Create(Path.Combine("Downloads", fn));
            await SendControlCommandAsync(0x002D);
            Log($"Streaming stretch to {fn}");
        }

        public async Task StopStretchStreamAsync()
        {
            if (ControlCharacteristic == null) return;
            await SendControlCommandAsync(0x002E);
            stretchFileStream?.Flush(); stretchFileStream?.Close(); stretchFileStream = null;
            Log("Stopped stretch stream.");
        }

        public async Task EraseFilesAsync() => await SendControlCommandAsync(0x002B);

        public void Disconnect()
        {
            StopScanning();
            try
            {
                ControlCharacteristic?.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                DataStreamCharacteristic?.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                DownloadCharacteristic?.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                _batterySession?.Dispose(); _batterySession = null;
                _connectedDevice?.Dispose();
                _batteryStartLocalTime = null; // reset elapsed base
                Log("Disconnected");
            }
            catch (Exception ex) { Log($"Disconnect error: {ex.Message}"); }
        }

        private T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
            finally { handle.Free(); }
        }

        private void Log(string msg) => LogMessage?.Invoke(msg);

        public void Dispose()
        {
            StopScanning();
            _batterySession?.Dispose(); _batterySession = null;
            _connectedDevice?.Dispose();
        }
    }
}
