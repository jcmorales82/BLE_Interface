using BLE_Interface.Controls;
using BLE_Interface.Helpers;
using BLE_Interface.Models;
using BLE_Interface.Services;
using BLE_Interface.Properties;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BLE_Interface.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly BleService _bleService;
        private int _imuSampleCounter = 0;
        private const int ImuDownsampleFactor = 5;

        // Log management
        private readonly ObservableCollection<string> _logList = new ObservableCollection<string>();
        private const int MaxLogLines = 100;

        // Breath charts (top priority)
        public DualSeriesChartControl BRChart { get; }
        public DualSeriesChartControl TVChart { get; }
        public DualSeriesChartControl MVChart { get; }

        // Chest charts
        public SkiaChartControl ChestRawChart { get; }
        public SkiaChartControl ChestNormChart { get; }

        // IMU charts (individual but displayed together)
        public SkiaChartControl AccXChart { get; }
        public SkiaChartControl AccYChart { get; }
        public SkiaChartControl AccZChart { get; }
        public SkiaChartControl GyrXChart { get; }
        public SkiaChartControl GyrYChart { get; }
        public SkiaChartControl GyrZChart { get; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<BleDeviceModel> Devices => _bleService.DiscoveredDevices;

        private BleDeviceModel _selectedDevice;
        public BleDeviceModel SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice != value)
                {
                    _selectedDevice = value;
                    OnPropertyChanged(nameof(SelectedDevice));
                }
            }
        }

        private int? _batteryLevel;
        public int? BatteryLevel
        {
            get => _batteryLevel;
            set
            {
                if (_batteryLevel != value)
                {
                    _batteryLevel = value;
                    OnPropertyChanged(nameof(BatteryLevel));
                }
            }
        }

        private string _logMessages = "";
        public string LogMessages
        {
            get => _logMessages;
            set { if (_logMessages != value) { _logMessages = value; OnPropertyChanged(nameof(LogMessages)); } }
        }

        private uint _latestCounter;
        private ushort _latestChestRaw;
        private ushort _latestChestNorm;
        private string _latestAccel = "—";
        private string _latestGyro = "—";
        private string _latestPL = "—";
        private uint _latestPressure;
        private short _latestTemperature;
        private double _latestAltitude;
        private ushort _latestStretch;
        private ushort _latestHeartRate;
        private ushort _latestBreathingRate;
        private ushort _latestCadence;
        private uint _latestBreathCounter;
        private float _latestRawBRInterval;
        private float _latestProcessedBRInterval;
        private ushort _latestRawTidalVolume;
        private ushort _latestProcessedTidalVolume;
        private ushort _latestRawMinuteVolume;
        private ushort _latestProcessedMinuteVolume;


        // Breath Timestamps data fields
        private uint _latestBreathTimestampsCounter = 0;
        private uint _latestTvValleyTimeIndex = 0;
        private ushort _latestTvValleyValue = 0;
        private uint _latestTvPeakTimeIndex = 0;
        private ushort _latestTvPeakValue = 0;
        public uint LatestBreathTimestampsCounter
        {
            get => _latestBreathTimestampsCounter;
            set { if (_latestBreathTimestampsCounter != value) { _latestBreathTimestampsCounter = value; OnPropertyChanged(nameof(LatestBreathTimestampsCounter)); } }
        }

        public uint LatestTvValleyTimeIndex
        {
            get => _latestTvValleyTimeIndex;
            set { if (_latestTvValleyTimeIndex != value) { _latestTvValleyTimeIndex = value; OnPropertyChanged(nameof(LatestTvValleyTimeIndex)); } }
        }

        public ushort LatestTvValleyValue
        {
            get => _latestTvValleyValue;
            set { if (_latestTvValleyValue != value) { _latestTvValleyValue = value; OnPropertyChanged(nameof(LatestTvValleyValue)); } }
        }

        public uint LatestTvPeakTimeIndex
        {
            get => _latestTvPeakTimeIndex;
            set { if (_latestTvPeakTimeIndex != value) { _latestTvPeakTimeIndex = value; OnPropertyChanged(nameof(LatestTvPeakTimeIndex)); } }
        }

        public ushort LatestTvPeakValue
        {
            get => _latestTvPeakValue;
            set { if (_latestTvPeakValue != value) { _latestTvPeakValue = value; OnPropertyChanged(nameof(LatestTvPeakValue)); } }
        }


        public uint LatestBreathCounter
        {
            get => _latestBreathCounter;
            set { if (_latestBreathCounter != value) { _latestBreathCounter = value; OnPropertyChanged(nameof(LatestBreathCounter)); } }
        }

        public float LatestRawBRInterval
        {
            get => _latestRawBRInterval;
            set { if (_latestRawBRInterval != value) { _latestRawBRInterval = value; OnPropertyChanged(nameof(LatestRawBRInterval)); } }
        }

        public float LatestProcessedBRInterval
        {
            get => _latestProcessedBRInterval;
            set { if (_latestProcessedBRInterval != value) { _latestProcessedBRInterval = value; OnPropertyChanged(nameof(LatestProcessedBRInterval)); } }
        }

        public ushort LatestRawTidalVolume
        {
            get => _latestRawTidalVolume;
            set { if (_latestRawTidalVolume != value) { _latestRawTidalVolume = value; OnPropertyChanged(nameof(LatestRawTidalVolume)); } }
        }

        public ushort LatestProcessedTidalVolume
        {
            get => _latestProcessedTidalVolume;
            set { if (_latestProcessedTidalVolume != value) { _latestProcessedTidalVolume = value; OnPropertyChanged(nameof(LatestProcessedTidalVolume)); } }
        }

        public ushort LatestRawMinuteVolume
        {
            get => _latestRawMinuteVolume;
            set { if (_latestRawMinuteVolume != value) { _latestRawMinuteVolume = value; OnPropertyChanged(nameof(LatestRawMinuteVolume)); } }
        }

        public ushort LatestProcessedMinuteVolume
        {
            get => _latestProcessedMinuteVolume;
            set { if (_latestProcessedMinuteVolume != value) { _latestProcessedMinuteVolume = value; OnPropertyChanged(nameof(LatestProcessedMinuteVolume)); } }
        }

        public uint LatestCounter
        {
            get => _latestCounter;
            set { if (_latestCounter != value) { _latestCounter = value; OnPropertyChanged(nameof(LatestCounter)); } }
        }

        public ushort LatestChestRaw
        {
            get => _latestChestRaw;
            set { if (_latestChestRaw != value) { _latestChestRaw = value; OnPropertyChanged(nameof(LatestChestRaw)); } }
        }

        public ushort LatestChestNormalized
        {
            get => _latestChestNorm;
            set { if (_latestChestNorm != value) { _latestChestNorm = value; OnPropertyChanged(nameof(LatestChestNormalized)); } }
        }

        public string LatestAccelerometer
        {
            get => _latestAccel;
            set { if (_latestAccel != value) { _latestAccel = value; OnPropertyChanged(nameof(LatestAccelerometer)); } }
        }

        public string LatestGyroscope
        {
            get => _latestGyro;
            set { if (_latestGyro != value) { _latestGyro = value; OnPropertyChanged(nameof(LatestGyroscope)); } }
        }

        public string LatestPlayerLoad
        {
            get => _latestPL;
            set { if (_latestPL != value) { _latestPL = value; OnPropertyChanged(nameof(LatestPlayerLoad)); } }
        }

        public uint LatestPressure
        {
            get => _latestPressure;
            set { if (_latestPressure != value) { _latestPressure = value; OnPropertyChanged(nameof(LatestPressure)); } }
        }

        public short LatestTemperature
        {
            get => _latestTemperature;
            set { if (_latestTemperature != value) { _latestTemperature = value; OnPropertyChanged(nameof(LatestTemperature)); } }
        }

        public double LatestAltitude
        {
            get => _latestAltitude;
            set { if (_latestAltitude != value) { _latestAltitude = value; OnPropertyChanged(nameof(LatestAltitude)); } }
        }

        public ushort LatestStretch
        {
            get => _latestStretch;
            set { if (_latestStretch != value) { _latestStretch = value; OnPropertyChanged(nameof(LatestStretch)); } }
        }

        public ushort LatestHeartRate
        {
            get => _latestHeartRate;
            set { if (_latestHeartRate != value) { _latestHeartRate = value; OnPropertyChanged(nameof(LatestHeartRate)); } }
        }

        public ushort LatestBreathingRate
        {
            get => _latestBreathingRate;
            set { if (_latestBreathingRate != value) { _latestBreathingRate = value; OnPropertyChanged(nameof(LatestBreathingRate)); } }
        }

        public ushort LatestCadence
        {
            get => _latestCadence;
            set { if (_latestCadence != value) { _latestCadence = value; OnPropertyChanged(nameof(LatestCadence)); } }
        }

        private string _dataFolderPath;
        public string DataFolderPath
        {
            get => _dataFolderPath;
            set
            {
                if (_dataFolderPath != value)
                {
                    _dataFolderPath = value;
                    OnPropertyChanged(nameof(DataFolderPath));
                    OnPropertyChanged(nameof(DataFolderDisplayName));
                }
            }
        }

        public string DataFolderDisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(DataFolderPath))
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") + " (default)";
                
                return DataFolderPath;
            }
        }

        public ICommand ScanCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand ConnectDeviceCommand { get; }
        public ICommand DisconnectDeviceCommand { get; }
        public ICommand SyncRTCCommand { get; }
        public ICommand HardwareInfoCommand { get; }
        public ICommand StartActivityCommand { get; }
        public ICommand StopActivityCommand { get; }
        public ICommand ListFilesCommand { get; }
        public ICommand DownloadAllCommand { get; }
        public ICommand StartStretchCommand { get; }
        public ICommand StopStretchCommand { get; }
        public ICommand EraseFilesCommand { get; }
        public ICommand FactoryCalCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand SelectDataFolderCommand { get; }

        public MainWindowViewModel()
        {
            _bleService = new BleService();
            
            // Load saved data folder path
            DataFolderPath = Settings.Default.DataFolderPath;

            _bleService.LogMessage += OnLogMessage;
            _bleService.DeviceConnected += OnDeviceConnected;
            _bleService.DeviceDisconnected += OnDeviceDisconnected;
            _bleService.BatteryLevelUpdated += OnBatteryLevelUpdated;
            _bleService.StretchDataReceived += OnStretchDataReceived;
            _bleService.ExtendedDataReceived += OnExtendedDataReceived;
            _bleService.DeviceDiscovered += OnDeviceDiscovered;
            _bleService.BreathDataReceived += OnBreathDataReceived;
            _bleService.BreathTimestampsReceived += OnBreathTimestampsReceived;

            BRChart = new DualSeriesChartControl
            {
                RawColor = SKColors.Red,
                ProcessedColor = SKColors.Cyan,
                WindowSize = 1500,
                SampleRate = 25.0,
                ShowGrid = true,
                ShowLabels = true,
                Title = "Breathing Rate (BR)",
                YAxisIncrements = new double[] { 5, 10, 20, 25, 50, 100 },
                AutoScaleY = false,
                MinY = 0,
                MaxY = 100
            };

            TVChart = new DualSeriesChartControl
            {
                RawColor = SKColors.Orange,
                ProcessedColor = SKColors.LimeGreen,
                WindowSize = 1500,
                SampleRate = 25.0,
                ShowGrid = true,
                ShowLabels = true,
                Title = "Tidal Volume (TV)"
            };

            MVChart = new DualSeriesChartControl
            {
                RawColor = SKColors.Yellow,
                ProcessedColor = SKColors.DeepSkyBlue,
                WindowSize = 1500,
                SampleRate = 25.0,
                ShowGrid = true,
                ShowLabels = true,
                Title = "Minute Ventilation (MV)"
            };

            ChestRawChart = new SkiaChartControl
            {
                LineColor = SKColors.Red,
                LineWidth = 1.5f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            ChestNormChart = new SkiaChartControl
            {
                LineColor = SKColors.Orange,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            AccXChart = new SkiaChartControl
            {
                LineColor = SKColors.Yellow,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            AccYChart = new SkiaChartControl
            {
                LineColor = SKColors.Cyan,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            AccZChart = new SkiaChartControl
            {
                LineColor = SKColors.Red,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            GyrXChart = new SkiaChartControl
            {
                LineColor = SKColors.Yellow,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            GyrYChart = new SkiaChartControl
            {
                LineColor = SKColors.Cyan,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            GyrZChart = new SkiaChartControl
            {
                LineColor = SKColors.Red,
                LineWidth = 2f,
                AutoScaleY = true,
                WindowSize = 1500,
                ShowGrid = true,
                ShowLabels = true,
                SampleRate = 25.0
            };

            ScanCommand = new RelayCommand(_ => StartScan());
            ConnectCommand = new RelayCommand(async _ => await ConnectToDevice(), _ => SelectedDevice != null);
            ConnectDeviceCommand = new RelayCommand(async param => await ConnectToDeviceAsync(param as BleDeviceModel));
            DisconnectDeviceCommand = new RelayCommand(async param => await DisconnectFromDeviceAsync(param as BleDeviceModel));
            SyncRTCCommand = new RelayCommand(async _ => await SyncRTC());
            HardwareInfoCommand = new RelayCommand(async _ => await GetHardwareInfo());
            StartActivityCommand = new RelayCommand(async _ => await StartActivity(), _ => SelectedDevice != null);
            StopActivityCommand = new RelayCommand(async _ => await StopActivity());
            ListFilesCommand = new RelayCommand(async _ => await ListFiles());
            DownloadAllCommand = new RelayCommand(async _ => await DownloadAllFiles());
            StartStretchCommand = new RelayCommand(async _ => await StartStretchStream(), _ => SelectedDevice != null);
            StopStretchCommand = new RelayCommand(async _ => await StopStretchStream());
            EraseFilesCommand = new RelayCommand(async _ => await EraseFiles());
            FactoryCalCommand = new RelayCommand(async _ => await DoFactoryCal());
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            SelectDataFolderCommand = new RelayCommand(_ => SelectDataFolder());
        }

        private void Log(string message)
        {
            OnLogMessage(message);
        }

        private void OnLogMessage(string message)
        {
            Services.Logging.Log.Info(message);

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                string timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";

                if (ShouldShowLog(message))
                {
                    _logList.Add(timestamped);

                    while (_logList.Count > MaxLogLines)
                        _logList.RemoveAt(0);

                    LogMessages = string.Join(Environment.NewLine, _logList);
                }
            }));
        }

        private bool ShouldShowLog(string message)
        {
            var text = message.ToLowerInvariant();

            bool hasBattery = text.Contains("battery");
            bool hasImu = text.Contains("imu");
            bool hasBreath = text.Contains("breath");
            bool hasStretch = text.Contains("stretch");
            bool hasPress = text.Contains("pressure");
            bool hasTemp = text.Contains("temp");
            bool hasHR = text.Contains("hr") || text.Contains("heart rate");
            bool isGeneral = !(hasBattery || hasImu || hasBreath || hasStretch || hasPress || hasTemp || hasHR);

            // Only show general logs (connection status, device info, etc.)
            return isGeneral;
        }


        private void ClearLogs()
        {
            _logList.Clear();
            LogMessages = "";
        }

        private void SelectDataFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select Data Folder";
                dialog.SelectedPath = !string.IsNullOrEmpty(DataFolderPath) ? DataFolderPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    DataFolderPath = dialog.SelectedPath;
                    Settings.Default.DataFolderPath = DataFolderPath;
                    Settings.Default.Save();
                    Log($"Data folder set to: {DataFolderPath}");
                }
            }
        }

        private void OnDeviceDiscovered(BleDeviceModel device)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!Devices.Contains(device)) Devices.Add(device);
            }));
        }

        private void OnDeviceConnected()
        {
            // Update connection state for the selected device
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (SelectedDevice != null)
                {
                    SelectedDevice.ConnectionState = Models.DeviceConnectionState.Connected;
                }
            });
        }

        private void OnDeviceDisconnected()
        {
            // Reset connection state for the selected device
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (SelectedDevice != null)
                {
                    SelectedDevice.ConnectionState = Models.DeviceConnectionState.Disconnected;
                }
            });
        }

        private void OnBatteryLevelUpdated(int level)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                BatteryLevel = level;
                if (SelectedDevice != null) SelectedDevice.BatteryPercent = level;
            }));
        }

        private void OnStretchDataReceived(uint counter, ushort value)
        {
            PerformanceMonitor.RecordEvent("StretchData");
            Application.Current.Dispatcher.BeginInvoke(new Action(() => { LatestStretch = value; }));
            ChestNormChart.AddPoint(counter, value);
        }

        private void OnBreathDataReceived(BleService.BreathDataPoint breath)
        {
            PerformanceMonitor.RecordEvent("BreathData");

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                LatestBreathCounter = breath.Counter;
                LatestRawBRInterval = breath.RawBRInterval;
                LatestProcessedBRInterval = breath.ProcessedBRInterval;
                LatestRawTidalVolume = breath.RawTidalVolume;
                LatestProcessedTidalVolume = breath.ProcessedTidalVolume;
                LatestRawMinuteVolume = breath.RawMinuteVolume;
                LatestProcessedMinuteVolume = breath.ProcessedMinuteVolume;
            }));

            BRChart.AddPoint(breath.Counter, breath.RawBRInterval, breath.ProcessedBRInterval);
            TVChart.AddPoint(breath.Counter, breath.RawTidalVolume, breath.ProcessedTidalVolume);
            MVChart.AddPoint(breath.Counter, breath.RawMinuteVolume, breath.ProcessedMinuteVolume);
        }
        private void OnBreathTimestampsReceived(BleService.BreathTimestampsDataPoint timestamps)
        {
            Log($"Breath Timestamps - Counter: {timestamps.Counter}, Valley Time: {timestamps.TvValleyTimeIndex}, Valley Val: {timestamps.TvValleyValue}, Peak Time: {timestamps.TvPeakTimeIndex}, Peak Val: {timestamps.TvPeakValue}");

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                LatestBreathTimestampsCounter = timestamps.Counter;
                LatestTvValleyTimeIndex = timestamps.TvValleyTimeIndex;
                LatestTvValleyValue = timestamps.TvValleyValue;
                LatestTvPeakTimeIndex = timestamps.TvPeakTimeIndex;
                LatestTvPeakValue = timestamps.TvPeakValue;
            }));
        }
        private void OnExtendedDataReceived(BleService.ExtendedDataPoint ext)
        {
            PerformanceMonitor.RecordEvent("ExtendedData");

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                LatestCounter = ext.TimestampRel;
                LatestChestRaw = ext.Chest;
                LatestChestNormalized = ext.ChestNormalized;
                if (ext.IMU != null && ext.IMU.Count > 0)
                {
                    LatestAccelerometer = $"{ext.IMU[0].AccX:F1},{ext.IMU[0].AccY:F1},{ext.IMU[0].AccZ:F1}";
                    LatestGyroscope = $"{ext.IMU[0].GyrX:F1},{ext.IMU[0].GyrY:F1},{ext.IMU[0].GyrZ:F1}";
                }
                if (ext.PlayerLoad != null && ext.PlayerLoad.Count > 0)
                {
                    LatestPlayerLoad = string.Join(",", ext.PlayerLoad);
                }
                LatestPressure = ext.Pressure;
                LatestTemperature = ext.Temperature;
                LatestAltitude = CalculateAltitude(ext.Pressure, ext.Temperature);
            }));

            ChestRawChart.AddPoint(ext.TimestampRel, ext.ChestNormalized);
            ChestNormChart.AddPoint(ext.TimestampRel, ext.Chest);

            if (ext.IMU != null && ext.IMU.Count > 0)
            {
                _imuSampleCounter++;
                if (_imuSampleCounter >= ImuDownsampleFactor)
                {
                    _imuSampleCounter = 0;
                    AccXChart.AddPoint(ext.TimestampRel, ext.IMU.Average(s => s.AccX));
                    AccYChart.AddPoint(ext.TimestampRel, ext.IMU.Average(s => s.AccY));
                    AccZChart.AddPoint(ext.TimestampRel, ext.IMU.Average(s => s.AccZ));
                    GyrXChart.AddPoint(ext.TimestampRel, ext.IMU.Average(s => s.GyrX));
                    GyrYChart.AddPoint(ext.TimestampRel, ext.IMU.Average(s => s.GyrY));
                    GyrZChart.AddPoint(ext.TimestampRel, ext.IMU.Average(s => s.GyrZ));
                }
            }
        }

        public void StartScan() => _bleService.StartScanning();

        public async Task ConnectToDevice()
        {
            if (SelectedDevice == null) return;
            
            // Set connecting state
            SelectedDevice.ConnectionState = Models.DeviceConnectionState.Connecting;
            Log($"Connecting to {SelectedDevice.Name}...");
            
            bool ok = await _bleService.ConnectAsync(SelectedDevice);
            
            if (ok)
            {
                SelectedDevice.ConnectionState = Models.DeviceConnectionState.Connected;
                // Don't log here - BleService already logs the connection
            }
            else
            {
                SelectedDevice.ConnectionState = Models.DeviceConnectionState.Disconnected;
                // BleService already logs the failure after retries
            }
        }

        public async Task ConnectToDeviceAsync(BleDeviceModel device)
        {
            if (device == null) return;

            // Set connecting state
            device.ConnectionState = Models.DeviceConnectionState.Connecting;
            Log($"Connecting to {device.Name}...");

            // Attempt connection
            bool success = await _bleService.ConnectAsync(device);

            if (success)
            {
                device.ConnectionState = Models.DeviceConnectionState.Connected;
                // Don't log here - BleService already logs the connection
            }
            else
            {
                device.ConnectionState = Models.DeviceConnectionState.Disconnected;
                // BleService already logs the failure after retries
            }
        }

        public async Task DisconnectFromDeviceAsync(BleDeviceModel device)
        {
            if (device == null) return;

            Log($"Disconnecting from {device.Name}...");

            try
            {
                await _bleService.DisconnectAsync();
                device.ConnectionState = Models.DeviceConnectionState.Disconnected;
                Log($"Disconnected from {device.Name}");
            }
            catch (Exception ex)
            {
                Log($"Disconnect failed: {ex.Message}");
            }
        }

        public async Task SyncRTC()
        {
            try
            {
                await _bleService.SyncRTCAsync();
                Log("RTC synced");
            }
            catch (Exception ex) { Log($"RTC sync failed: {ex.Message}"); }
        }

        public async Task GetHardwareInfo()
        {
            try
            {
                var data = await _bleService.GetHardwareInfoAsync();
                var info = HardwareInfo.Parse(data);

                // Log each line separately so filtering works correctly
                string output = info.ToString();
                var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    Log(line.Trim());
                }
            }
            catch (Exception ex)
            {
                Log($"Hardware info failed: {ex.Message}");
            }
        }

        public async Task StartActivity()
        {
            BRChart.Clear();
            TVChart.Clear();
            MVChart.Clear();
            ChestRawChart.Clear();
            ChestNormChart.Clear();
            AccXChart.Clear();
            AccYChart.Clear();
            AccZChart.Clear();
            GyrXChart.Clear();
            GyrYChart.Clear();
            GyrZChart.Clear();
            _imuSampleCounter = 0;

            try
            {
                // Start data logging
                if (SelectedDevice != null)
                {
                    _bleService.StartDataLogging(SelectedDevice.Name, DataFolderPath);
                }

                await _bleService.StartActivityAsync();
                Log("Activity started");
            }
            catch (Exception ex) { Log($"Start activity failed: {ex.Message}"); }
        }

        public async Task StopActivity()
        {
            try
            {
                await _bleService.StopActivityAsync();
                
                // Stop data logging
                _bleService.StopDataLogging();
                
                Log("Activity stopped");
            }
            catch (Exception ex) { Log($"Stop activity failed: {ex.Message}"); }
        }

        public async Task ListFiles()
        {
            try
            {
                await _bleService.ListFilesAsync();
                var files = _bleService.FileList;
                Log($"📂 {files.Count} file(s)");
                foreach (var f in files) Log(f.ToString());
            }
            catch (Exception ex) { Log($"List files failed: {ex.Message}"); }
        }

        public async Task DownloadAllFiles()
        {
            try
            {
                var files = _bleService.FileList.ToList();
                Log($"Downloading {files.Count} file(s)...");
                foreach (var file in files) await _bleService.DownloadFileAsync(file);
                Log("All files downloaded");
            }
            catch (Exception ex) { Log($"Download files failed: {ex.Message}"); }
        }

        public async Task StartStretchStream()
        {
            if (SelectedDevice == null) return;

            BRChart.Clear();
            TVChart.Clear();
            MVChart.Clear();
            ChestRawChart.Clear();
            ChestNormChart.Clear();
            AccXChart.Clear();
            AccYChart.Clear();
            AccZChart.Clear();
            GyrXChart.Clear();
            GyrYChart.Clear();
            GyrZChart.Clear();
            _imuSampleCounter = 0;

            try
            {
                await _bleService.StartStretchStreamAsync(SelectedDevice.Name);
                Log("Stretch streaming started");
            }
            catch (Exception ex) { Log($"Start stretch failed: {ex.Message}"); }
        }

        public async Task StopStretchStream()
        {
            try
            {
                await _bleService.StopStretchStreamAsync();
                Log("Stretch streaming stopped");
            }
            catch (Exception ex) { Log($"Stop stretch failed: {ex.Message}"); }
        }

        public async Task EraseFiles()
        {
            try
            {
                await _bleService.EraseFilesAsync();
                Log("Files erased");
            }
            catch (Exception ex) { Log($"Erase files failed: {ex.Message}"); }
        }

        private async Task DoFactoryCal()
        {
            try
            {
                Log("Factory calibration starting...");
                await _bleService.FactoryCalibrationAsync();
                Log("Factory calibration completed");
                await Task.Delay(5000);
                Log("Factory calibration successful");
            }
            catch (Exception ex) { Log($"Factory calibration failed: {ex.Message}"); }
        }

        /// <summary>
        /// Calculate altitude from pressure and temperature using barometric formula
        /// h = 44330 * (1 - (p/p0)^0.1903)
        /// where p0 = 101325 Pa (standard sea level pressure)
        /// </summary>
        private double CalculateAltitude(uint pressurePa, short temperatureC)
        {
            const double p0 = 101325.0; // Standard sea level pressure in Pa

            double pressureRatio = pressurePa / p0;
            double altitude = 44330.0 * (1.0 - Math.Pow(pressureRatio, 0.1903));

            return altitude;
        }

        public void Dispose()
        {
            _bleService.LogMessage -= OnLogMessage;
            _bleService.DeviceConnected -= OnDeviceConnected;
            _bleService.DeviceDisconnected -= OnDeviceDisconnected;
            _bleService.BatteryLevelUpdated -= OnBatteryLevelUpdated;
            _bleService.StretchDataReceived -= OnStretchDataReceived;
            _bleService.ExtendedDataReceived -= OnExtendedDataReceived;
            _bleService.DeviceDiscovered -= OnDeviceDiscovered;
            _bleService.BreathDataReceived -= OnBreathDataReceived;
            _bleService.BreathTimestampsReceived -= OnBreathTimestampsReceived;

            BRChart?.Dispose();
            TVChart?.Dispose();
            MVChart?.Dispose();
            ChestRawChart?.Dispose();
            ChestNormChart?.Dispose();
            AccXChart?.Dispose();
            AccYChart?.Dispose();
            AccZChart?.Dispose();
            GyrXChart?.Dispose();
            GyrYChart?.Dispose();
            GyrZChart?.Dispose();

            _bleService?.Dispose();
        }
    }
}