using BLE_Interface.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BLE_Interface.Services
{
    public class BleService : IDisposable
    {
        // Service/Characteristic UUIDs
        private static readonly Guid CustomServiceUuid = new Guid("40B50000-30B5-11E5-A151-FEFF819CDC90");
        private static readonly Guid DataStreamCharUuid = new Guid("40B50004-30B5-11E5-A151-FEFF819CDC90");
        private static readonly Guid ControlCharUuid = new Guid("40B50007-30B5-11E5-A151-FEFF819CDC90");
        private static readonly Guid DownloadCharUuid = new Guid("40B50001-30B5-11E5-A151-FEFF819CDC90");

        // Connection state
        private BluetoothLEDevice _device;
        private GattCharacteristic _controlChar;
        private GattCharacteristic _dataStreamChar;
        private GattCharacteristic _downloadChar;
        private BatteryServiceSession _batterySession;
        private BatteryLogger _batteryLogger;
        private DateTime? _batteryStartTime;

        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected;
        public ulong DeviceAddress { get; private set; }

        // Scanning
        private readonly BluetoothLEAdvertisementWatcher _watcher;
        private readonly Dictionary<ulong, BleDeviceModel> _deviceMap = new Dictionary<ulong, BleDeviceModel>();
        public ObservableCollection<BleDeviceModel> DiscoveredDevices { get; } = new ObservableCollection<BleDeviceModel>();

        // Command tracking
        private ushort _nextTag = 1;
        private readonly Dictionary<ushort, TaskCompletionSource<byte[]>> _pendingCommands = new Dictionary<ushort, TaskCompletionSource<byte[]>>();
        private readonly Dictionary<ushort, ushort> _tagToOpcode = new Dictionary<ushort, ushort>();
        private readonly SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1);

        // File operations
        private readonly List<FileEntry> _fileList = new List<FileEntry>();
        public IReadOnlyList<FileEntry> FileList => _fileList.AsReadOnly();
        private CancellationTokenSource _downloadCts;
        private StreamWriter _downloadWriter;
        private readonly List<byte> _downloadBuffer = new List<byte>();
        private bool _wroteBreathHeader, _wroteImuHeader, _wroteHrHeader;

        // Stretch streaming
        private FileStream _stretchStream;

        // Events - ALL fire on background threads
        public event Action<string> LogMessage;
        public event Action DeviceConnected;
        public event Action DeviceDisconnected;
        public event Action<int> BatteryLevelUpdated;
        public event Action<uint, ushort> StretchDataReceived;
        public event Action<ExtendedDataPoint> ExtendedDataReceived;
        public event Action<List<IMUSample>> IMUSamplesReceived;
        public event Action<BleDeviceModel> DeviceDiscovered;
        public event Action<BreathDataPoint> BreathDataReceived;
        public event Action<BreathTimestampsDataPoint> BreathTimestampsReceived;

        // Helper classes
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

        public class BreathDataPoint
        {
            public uint Counter { get; set; }
            public float RawBRInterval { get; set; }
            public float ProcessedBRInterval { get; set; }
            public ushort RawTidalVolume { get; set; }
            public ushort ProcessedTidalVolume { get; set; }
            public ushort RawMinuteVolume { get; set; }
            public ushort ProcessedMinuteVolume { get; set; }
        }

        public class BreathTimestampsDataPoint
        {
            public uint Counter { get; set; }
            public uint TvValleyTimeIndex { get; set; }
            public ushort TvValleyValue { get; set; }
            public uint TvPeakTimeIndex { get; set; }
            public ushort TvPeakValue { get; set; }
        }

        public BleService()
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Stopped += (s, e) => { };
        }

        // ===== SCANNING =====

        public void StartScanning()
        {
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                return;

            lock (_deviceMap)
            {
                _deviceMap.Clear();
            }

            DiscoveredDevices.Clear();
            _watcher.Start();
            Log("🔍 BLE scan started");
        }

        public void StopScanning()
        {
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Stop();
            }
        }

        // Add this field to track known device addresses
        private readonly HashSet<ulong> _knownDeviceAddresses = new HashSet<ulong>();

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            // Check if this device is ours (has our service OR our manufacturer ID)
            bool hasOurService = args.Advertisement.ServiceUuids.Contains(CustomServiceUuid);
            bool hasOurManufacturerData = args.Advertisement.ManufacturerData.Any(m => m.CompanyId == 0xF191);

            if (!hasOurService && !hasOurManufacturerData)
                return;

            ulong addr = args.BluetoothAddress;
            string hex = addr.ToString("X").PadLeft(12, '0');
            string shortId = hex.Substring(hex.Length - 4);
            string name = $"TYME-{shortId}";
            int rssi = args.RawSignalStrengthInDBm;

            // Parse battery from manufacturer data
            int? batteryPercent = null;
            foreach (var mfgData in args.Advertisement.ManufacturerData)
            {
                if (mfgData.CompanyId == 0xF191 && mfgData.Data.Length >= 1)
                {
                    using (var reader = DataReader.FromBuffer(mfgData.Data))
                    {
                        batteryPercent = reader.ReadByte();
                    }
                    break;
                }
            }

            lock (_deviceMap)
            {
                if (!_deviceMap.ContainsKey(addr))
                {
                    // New device - only add if it has the service UUID
                    if (hasOurService)
                    {
                        var dev = new BleDeviceModel { Address = hex, Name = name, Rssi = rssi };
                        if (batteryPercent.HasValue) dev.BatteryPercent = batteryPercent.Value;
                        _deviceMap[addr] = dev;
                        DeviceDiscovered?.Invoke(dev);
                        Log($"Discovered: {name} (RSSI {rssi})");
                    }
                }
                else
                {
                    // Update existing device
                    _deviceMap[addr].Rssi = rssi;
                    if (batteryPercent.HasValue)
                    {
                        _deviceMap[addr].BatteryPercent = batteryPercent.Value;
                    }
                }
            }
        }
        // ===== CONNECTION =====

        public async Task<bool> ConnectAsync(BleDeviceModel deviceModel, CancellationToken ct = default)
        {
            await _connectionLock.WaitAsync(ct);
            try
            {
                StopScanning();
                await DisconnectInternalAsync();

                ulong addr = Convert.ToUInt64(deviceModel.Address, 16);

                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(addr).AsTask(ct);
                if (_device == null)
                {
                    Log("❌ Failed to connect");
                    return false;
                }

                DeviceAddress = addr;
                _device.ConnectionStatusChanged += OnConnectionStatusChanged;

                await Task.Delay(200, ct);

                var svcResult = await RetryAsync(
                    async () => await _device.GetGattServicesForUuidAsync(CustomServiceUuid, BluetoothCacheMode.Uncached),
                    retries: 3,
                    delayMs: 200,
                    ct: ct
                );

                if (svcResult?.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                {
                    Log("❌ Service not found");
                    await DisconnectInternalAsync();
                    return false;
                }

                var service = svcResult.Services[0];

                _dataStreamChar = await SetupCharacteristicAsync(service, DataStreamCharUuid, OnDataStreamChanged, ct);
                _controlChar = await SetupCharacteristicAsync(service, ControlCharUuid, OnControlChanged, ct);
                _downloadChar = await SetupCharacteristicAsync(service, DownloadCharUuid, OnDownloadChanged, ct);

                if (_dataStreamChar == null || _controlChar == null || _downloadChar == null)
                {
                    Log("❌ Failed to setup characteristics");
                    await DisconnectInternalAsync();
                    return false;
                }

                await SetupBatteryServiceAsync(deviceModel.Name);

                Log($"✅ Connected to {deviceModel.Name}");
                DeviceConnected?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ Connection error: {ex.Message}");
                await DisconnectInternalAsync();
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Log("Device disconnected");
                _ = DisconnectAsync();
                DeviceDisconnected?.Invoke();
            }
        }

        private async Task<GattCharacteristic> SetupCharacteristicAsync(
            GattDeviceService service,
            Guid charUuid,
            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler,
            CancellationToken ct)
        {
            try
            {
                var result = await service.GetCharacteristicsForUuidAsync(charUuid);
                if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
                    return null;

                var ch = result.Characteristics[0];
                ch.ValueChanged += handler;

                var status = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (status != GattCommunicationStatus.Success)
                {
                    await Task.Delay(100, ct);
                    await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                }

                return ch;
            }
            catch
            {
                return null;
            }
        }

        private async Task SetupBatteryServiceAsync(string deviceName)
        {
            try
            {
                _batteryLogger = new BatteryLogger(deviceName);
                _batteryStartTime = null;

                _batterySession = await BatteryServiceSession.CreateAsync(_device, level =>
                {
                    BatteryLevelUpdated?.Invoke(level);
                });
            }
            catch (Exception ex)
            {
                Log($"⚠️ Battery service unavailable: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                await DisconnectInternalAsync();
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task DisconnectInternalAsync()
        {
            if (_controlChar != null)
            {
                try
                {
                    _controlChar.ValueChanged -= OnControlChanged;
                    await _controlChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
                _controlChar = null;
            }

            if (_dataStreamChar != null)
            {
                try
                {
                    _dataStreamChar.ValueChanged -= OnDataStreamChanged;
                    await _dataStreamChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
                _dataStreamChar = null;
            }

            if (_downloadChar != null)
            {
                try
                {
                    _downloadChar.ValueChanged -= OnDownloadChanged;
                    await _downloadChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
                _downloadChar = null;
            }

            _batterySession?.Dispose();
            _batterySession = null;
            _batteryLogger = null;
            _batteryStartTime = null;

            if (_device != null)
            {
                _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _device.Dispose();
                _device = null;
            }

            DeviceAddress = 0;
        }

        // ===== CHARACTERISTIC HANDLERS =====

        private async void OnControlChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                reader.ByteOrder = ByteOrder.LittleEndian;

                ushort respCode = reader.ReadUInt16();
                ushort tag = reader.ReadUInt16();

                var payload = new byte[args.CharacteristicValue.Length - 4];
                reader.ReadBytes(payload);

                // Battery status
                if (respCode == 0x4002)
                {
                    await HandleBatteryStatusAsync(payload);
                    return;
                }

                // Regular command response
                lock (_pendingCommands)
                {
                    if (_pendingCommands.TryGetValue(tag, out var tcs))
                    {
                        _pendingCommands.Remove(tag);
                        _tagToOpcode.Remove(tag);
                        tcs.TrySetResult(payload);
                    }
                }

                LogResponse(respCode, tag);
            }
            catch (Exception ex)
            {
                Log($"⚠️ Control handler error: {ex.Message}");
            }
        }

        private async Task HandleBatteryStatusAsync(byte[] payload)
        {
            if (payload.Length < 4) return;

            uint charge = BitConverter.ToUInt32(payload, 0);
            int? battPct = null;

            if (_batterySession != null)
            {
                try
                {
                    var level = await _batterySession.ReadAsync();
                    if (level.HasValue)
                    {
                        battPct = level.Value;
                        BatteryLevelUpdated?.Invoke(level.Value);
                    }
                }
                catch { }
            }

            var now = DateTime.Now;
            if (!_batteryStartTime.HasValue)
                _batteryStartTime = now;

            var elapsed = now - _batteryStartTime.Value;
            int totalMinutes = (int)Math.Round(elapsed.TotalMinutes);
            string hhmm = $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";

            try
            {
                _batteryLogger?.Append(hhmm, (int)charge, battPct);
            }
            catch { }
        }

        private void OnDataStreamChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                var buffer = args.CharacteristicValue;
                byte[] raw = new byte[buffer.Length];
                DataReader.FromBuffer(buffer).ReadBytes(raw);

                if (raw.Length < 1) return;

                ProcessDataPacket(raw);
            }
            catch (Exception ex)
            {
                Log($"⚠️ DataStream error: {ex.Message}");
            }
        }

        private void ProcessDataPacket(byte[] raw)
        {
            byte type = raw[0];

            switch (type)
            {
                case 0:
                    if (raw.Length >= 85) ProcessExtendedData(raw);
                    break;
                case 1:
                    if (raw.Length >= 17) ProcessBreathingData(raw);
                    break;
                case 2:
                    if (raw.Length >= 13) ProcessImuData(raw);
                    break;
                case 3:
                    if (raw.Length >= 7) ProcessStretchData(raw);
                    break;
                case 4:
                    if (raw.Length >= 11) ProcessPressureTempData(raw);
                    break;
                case 5:
                    if (raw.Length >= 7) ProcessHeartRateData(raw);
                    break;
                case 6:
                    if (raw.Length >= 17) ProcessBreathTimestampsData(raw);
                    break;
            }
        }

        private void ProcessExtendedData(byte[] raw)
        {
            uint counter = BitConverter.ToUInt32(raw, 1);
            ushort chestRaw = BitConverter.ToUInt16(raw, 5);
            ushort chestNorm = BitConverter.ToUInt16(raw, 7);

            var imuList = new List<IMUSample>(5);
            int offset = 9;
            for (int i = 0; i < 5; i++)
            {
                imuList.Add(new IMUSample
                {
                    AccX = BitConverter.ToInt16(raw, offset + 0),
                    AccY = BitConverter.ToInt16(raw, offset + 2),
                    AccZ = BitConverter.ToInt16(raw, offset + 4),
                    GyrX = BitConverter.ToInt16(raw, offset + 6),
                    GyrY = BitConverter.ToInt16(raw, offset + 8),
                    GyrZ = BitConverter.ToInt16(raw, offset + 10)
                });
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

            var ext = new ExtendedDataPoint
            {
                TimestampRel = counter,
                Chest = chestRaw,
                ChestNormalized = chestNorm,
                IMU = imuList,
                PlayerLoad = loadList,
                Pressure = pressure,
                Temperature = temperature
            };

            ExtendedDataReceived?.Invoke(ext);
        }

        private void ProcessBreathingData(byte[] raw)
        {
            var breath = new BreathDataPoint
            {
                Counter = BitConverter.ToUInt32(raw, 1),
                RawBRInterval = BitConverter.ToUInt16(raw, 5) / 10f,
                ProcessedBRInterval = BitConverter.ToUInt16(raw, 7) / 10f,
                RawTidalVolume = BitConverter.ToUInt16(raw, 9),
                ProcessedTidalVolume = BitConverter.ToUInt16(raw, 11),
                RawMinuteVolume = BitConverter.ToUInt16(raw, 13),
                ProcessedMinuteVolume = BitConverter.ToUInt16(raw, 15)
            };

            BreathDataReceived?.Invoke(breath);
        }
        private void ProcessBreathTimestampsData(byte[] raw)
        {
            var timestamps = new BreathTimestampsDataPoint
            {
                Counter = BitConverter.ToUInt32(raw, 1),
                TvValleyTimeIndex = BitConverter.ToUInt32(raw, 5),
                TvValleyValue = BitConverter.ToUInt16(raw, 9),
                TvPeakTimeIndex = BitConverter.ToUInt32(raw, 11),
                TvPeakValue = BitConverter.ToUInt16(raw, 15),
            };

            BreathTimestampsReceived?.Invoke(timestamps);
        }

        private void ProcessImuData(byte[] raw)
        {
            uint counter = BitConverter.ToUInt32(raw, 1);
            ushort cadence = BitConverter.ToUInt16(raw, 5);
            uint stepTime = BitConverter.ToUInt32(raw, 7);
            ushort playerLoad = BitConverter.ToUInt16(raw, 11);
        }

        private void ProcessStretchData(byte[] raw)
        {
            uint counter = BitConverter.ToUInt32(raw, 1);
            ushort normalized = BitConverter.ToUInt16(raw, 5);

            StretchDataReceived?.Invoke(counter, normalized);

            if (_stretchStream != null)
            {
                var line = Encoding.UTF8.GetBytes($"{counter},{normalized}\n");
                _stretchStream.Write(line, 0, line.Length);
            }
        }

        private void ProcessPressureTempData(byte[] raw)
        {
            uint counter = BitConverter.ToUInt32(raw, 1);
            uint pressure = BitConverter.ToUInt32(raw, 5);
            short temp = BitConverter.ToInt16(raw, 9);
        }

        private void ProcessHeartRateData(byte[] raw)
        {
            uint ts = BitConverter.ToUInt32(raw, 1);
            ushort hr = BitConverter.ToUInt16(raw, 5);
        }

        private void OnDownloadChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (_downloadWriter == null) return;

            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();
            _ = Task.Delay(1000, _downloadCts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    _downloadWriter?.Close();
                    _downloadWriter = null;
                    Log("✅ Download complete");
                }
            });

            var incoming = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(incoming);
            _downloadBuffer.AddRange(incoming);

            ProcessDownloadBuffer();
        }

        private void ProcessDownloadBuffer()
        {
            int idx = 0;
            while (idx < _downloadBuffer.Count)
            {
                byte type = _downloadBuffer[idx];
                int len = type switch { 1 => 16, 2 => 12, 5 => 6, _ => -1 };
                if (len < 0) { _downloadBuffer.Clear(); return; }
                if (idx + 1 + len > _downloadBuffer.Count) break;

                var payload = _downloadBuffer.Skip(idx + 1).Take(len).ToArray();
                idx += 1 + len;

                WriteDownloadRecord(type, payload);
            }

            if (idx > 0)
                _downloadBuffer.RemoveRange(0, idx);
        }

        private void WriteDownloadRecord(byte type, byte[] payload)
        {
            switch (type)
            {
                case 1:
                    if (!_wroteBreathHeader)
                    {
                        _downloadWriter.WriteLine("[Breathing]");
                        _wroteBreathHeader = true;
                    }
                    _downloadWriter.WriteLine($"{BitConverter.ToUInt32(payload, 0)},{BitConverter.ToUInt16(payload, 4)},{BitConverter.ToUInt16(payload, 6)},{BitConverter.ToUInt16(payload, 8)},{BitConverter.ToUInt16(payload, 10)},{BitConverter.ToUInt16(payload, 12)},{BitConverter.ToUInt16(payload, 14)}");
                    break;

                case 2:
                    if (!_wroteImuHeader)
                    {
                        _downloadWriter.WriteLine("[IMU]");
                        _wroteImuHeader = true;
                    }
                    _downloadWriter.WriteLine($"{BitConverter.ToUInt32(payload, 0)},{BitConverter.ToUInt16(payload, 4)},{BitConverter.ToUInt32(payload, 6)},{BitConverter.ToUInt16(payload, 10)}");
                    break;

                case 5:
                    if (!_wroteHrHeader)
                    {
                        _downloadWriter.WriteLine("[HR]");
                        _wroteHrHeader = true;
                    }
                    _downloadWriter.WriteLine($"{BitConverter.ToUInt32(payload, 0)},{BitConverter.ToUInt16(payload, 4)}");
                    break;
            }
        }

        // ===== COMMANDS =====

        public async Task<byte[]> SendCommandAsync(ushort opcode, uint? param = null, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected");

            await _commandLock.WaitAsync(ct);
            try
            {
                ushort tag = _nextTag++;
                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(opcode);
                    w.Write(tag);
                    if (param.HasValue)
                        w.Write(param.Value);
                }

                var tcs = new TaskCompletionSource<byte[]>();
                lock (_pendingCommands)
                {
                    _pendingCommands[tag] = tcs;
                    _tagToOpcode[tag] = opcode;
                }

                await _controlChar.WriteValueAsync(ms.ToArray().AsBuffer());
                Log($"Sent 0x{opcode:X4} tag {tag}");

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                var completedTask = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token)
                );

                if (completedTask != tcs.Task)
                {
                    lock (_pendingCommands)
                    {
                        _pendingCommands.Remove(tag);
                        _tagToOpcode.Remove(tag);
                    }
                    throw new TimeoutException($"Command 0x{opcode:X4} timed out");
                }

                return await tcs.Task;
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public Task SyncRTCAsync(CancellationToken ct = default)
            => SendCommandAsync(0x002C, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ct);

        public Task StartActivityAsync(CancellationToken ct = default)
            => SendCommandAsync(0x0020, null, ct);

        public Task StopActivityAsync(CancellationToken ct = default)
            => SendCommandAsync(0x0021, null, ct);

        public Task EraseFilesAsync(CancellationToken ct = default)
            => SendCommandAsync(0x002B, null, ct);

        public Task<byte[]> GetHardwareInfoAsync(CancellationToken ct = default)
            => SendCommandAsync(0x0001, null, ct);

        public Task FactoryCalibrationAsync(CancellationToken ct = default)
            => SendCommandAsync(0x010A, null, ct);

        public async Task ListFilesAsync(CancellationToken ct = default)
        {
            var data = await SendCommandAsync(0x0109, null, ct);
            _fileList.Clear();
            int sz = Marshal.SizeOf<FileEntry>();
            for (int i = 0; i < data.Length; i += sz)
            {
                _fileList.Add(ByteArrayToStructure<FileEntry>(data.Skip(i).Take(sz).ToArray()));
            }
            Log($"📂 {_fileList.Count} files");
        }

        public async Task DownloadFileAsync(FileEntry file, CancellationToken ct = default)
        {
            Directory.CreateDirectory("Downloads");
            string path = Path.Combine("Downloads", file.GetFilename().Replace(".bin", ".txt"));
            int counter = 1;
            while (File.Exists(path))
                path = Path.Combine("Downloads", $"file_{file.timestamp}_{counter++}.txt");

            _downloadWriter = new StreamWriter(path, false, Encoding.UTF8);
            _downloadBuffer.Clear();
            _wroteBreathHeader = _wroteImuHeader = _wroteHrHeader = false;

            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();

            Log($"Downloading file {file.timestamp}");
            await SendCommandAsync(0x0108, file.timestamp, ct);

            while (_downloadWriter != null && !ct.IsCancellationRequested)
                await Task.Delay(100, ct);
        }

        public async Task StartStretchStreamAsync(string deviceName, CancellationToken ct = default)
        {
            Directory.CreateDirectory("Downloads");
            uint epoch = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string safe = deviceName.Replace(':', '_').Replace(' ', '_');
            string filename = $"{safe}_{epoch}_raw.txt";
            _stretchStream = File.Create(Path.Combine("Downloads", filename));
            await SendCommandAsync(0x002D, null, ct);
            Log($"Streaming stretch to {filename}");
        }

        public async Task StopStretchStreamAsync(CancellationToken ct = default)
        {
            await SendCommandAsync(0x002E, null, ct);
            _stretchStream?.Flush();
            _stretchStream?.Close();
            _stretchStream = null;
            Log("Stopped stretch stream");
        }

        // ===== HELPERS =====

        private async Task<T> RetryAsync<T>(Func<Task<T>> action, int retries, int delayMs, CancellationToken ct)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    var result = await action();
                    if (result != null) return result;
                }
                catch { }

                if (i < retries - 1)
                    await Task.Delay(delayMs, ct);
            }
            return default;
        }

        private T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
            finally { handle.Free(); }
        }

        private void LogResponse(ushort respCode, ushort tag)
        {
            var codes = new Dictionary<ushort, string>
            {
                {0x8000, "SUCCESS"}, {0x8100, "INVALID_REQ"}, {0x8200, "INVALID_PARAM"},
                {0x8300, "NOT_FOUND"}, {0x8400, "ERROR"}, {0x8500, "BUSY"},
                {0x8600, "LOCKED"}, {0x8700, "FORBIDDEN"}, {0x8800, "NO_MEM"}
            };

            string codeName = codes.TryGetValue(respCode, out var name) ? name : $"0x{respCode:X4}";
            Log($"Response: {codeName}");
        }

        private void Log(string message) => LogMessage?.Invoke(message);

        public void Dispose()
        {
            StopScanning();
            _ = DisconnectAsync();
            _connectionLock?.Dispose();
            _commandLock?.Dispose();
        }

        // ===== BATTERY SERVICE HELPER =====

        private sealed class BatteryServiceSession : IDisposable
        {
            private GattCharacteristic _char;
            public event Action<byte> BatteryLevelChanged;

            private BatteryServiceSession(GattCharacteristic ch) => _char = ch;

            public static async Task<BatteryServiceSession> CreateAsync(BluetoothLEDevice dev, Action<byte> onLevel)
            {
                var svcResult = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
                if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                    return null;

                var chrResult = await svcResult.Services[0].GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
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
                    _char.ValueChanged += OnValueChanged;
                    await _char.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    var val = await ReadAsync();
                    if (val.HasValue) BatteryLevelChanged?.Invoke(val.Value);
                }
                catch { }
            }

            private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
            {
                try
                {
                    var dr = DataReader.FromBuffer(args.CharacteristicValue);
                    BatteryLevelChanged?.Invoke(dr.ReadByte());
                }
                catch { }
            }

            public async Task<byte?> ReadAsync()
            {
                try
                {
                    var result = await _char.ReadValueAsync();
                    if (result.Status == GattCommunicationStatus.Success)
                    {
                        var dr = DataReader.FromBuffer(result.Value);
                        return dr.ReadByte();
                    }
                }
                catch { }
                return null;
            }

            public void Dispose()
            {
                if (_char != null)
                {
                    try { _char.ValueChanged -= OnValueChanged; } catch { }
                    _char = null;
                }
            }
        }

        private sealed class BatteryLogger
        {
            private readonly string _filePath;
            private readonly object _lock = new object();

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
    }
}