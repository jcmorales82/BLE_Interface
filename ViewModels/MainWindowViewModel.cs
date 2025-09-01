using BLE_Interface.Models;
using BLE_Interface.SensorFusion;  // for MadgwickAHRS
using BLE_Interface.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D; // for Quaternion (WPF)
using System.Collections.Generic;
using BLE_Interface.Helpers;
using System.Text; // <-- added for StringBuilder

namespace BLE_Interface.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // maximum number of points to keep in each series
        private const int MaxDataPoints = 1000;

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        // BLE service
        private readonly BleService _bleService;

        // Devices list
        public ObservableCollection<BleDeviceModel> Devices => _bleService.DiscoveredDevices;
        private readonly MadgwickAHRS _madgwick = new MadgwickAHRS(1.0f / 125.0f);

        private Quaternion _orientation = Quaternion.Identity;
        public Quaternion Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation != value)
                {
                    _orientation = value;
                    OnPropertyChanged(nameof(Orientation));
                }
            }
        }

        private BleDeviceModel _selectedDevice;
        public BleDeviceModel SelectedDevice
        {
            get => _selectedDevice;
            set { if (_selectedDevice != value) { _selectedDevice = value; OnPropertyChanged(nameof(SelectedDevice)); } }
        }

        // Log (view) + raw store for filtering
        private string _log;
        public string Log
        {
            get => _log;
            set { if (_log != value) { _log = value; OnPropertyChanged(nameof(Log)); } }
        }

        // --- Log filtering state ---
        private readonly List<string> _allLogs = new();  // raw lines with timestamps

        private bool _showBattery = true;
        public bool ShowBattery
        {
            get => _showBattery;
            set { if (_showBattery != value) { _showBattery = value; OnPropertyChanged(nameof(ShowBattery)); RebuildLogView(); } }
        }

        private bool _showIMU;
        public bool ShowIMU
        {
            get => _showIMU;
            set { if (_showIMU != value) { _showIMU = value; OnPropertyChanged(nameof(ShowIMU)); RebuildLogView(); } }
        }

        private bool _showBreathing;
        public bool ShowBreathing
        {
            get => _showBreathing;
            set { if (_showBreathing != value) { _showBreathing = value; OnPropertyChanged(nameof(ShowBreathing)); RebuildLogView(); } }
        }

        private bool _showStretch;
        public bool ShowStretch
        {
            get => _showStretch;
            set { if (_showStretch != value) { _showStretch = value; OnPropertyChanged(nameof(ShowStretch)); RebuildLogView(); } }
        }

        private bool _showPressureTemp;
        public bool ShowPressureTemp
        {
            get => _showPressureTemp;
            set { if (_showPressureTemp != value) { _showPressureTemp = value; OnPropertyChanged(nameof(ShowPressureTemp)); RebuildLogView(); } }
        }

        private bool _showHR;
        public bool ShowHR
        {
            get => _showHR;
            set { if (_showHR != value) { _showHR = value; OnPropertyChanged(nameof(ShowHR)); RebuildLogView(); } }
        }

        private bool _showGeneral;  // discovery, connect, files, scan, etc.
        public bool ShowGeneral
        {
            get => _showGeneral;
            set { if (_showGeneral != value) { _showGeneral = value; OnPropertyChanged(nameof(ShowGeneral)); RebuildLogView(); } }
        }

        // Optional header/toolbar binding for current battery level of connected device
        private int? _batteryLevel;
        public int? BatteryLevel
        {
            get => _batteryLevel;
            set { if (_batteryLevel != value) { _batteryLevel = value; OnPropertyChanged(nameof(BatteryLevel)); } }
        }

        // Extended-mode live values
        private uint _latestCounter;
        private ushort _latestChestRaw;
        private ushort _latestChestNorm;
        private string _latestAccel;
        private string _latestGyro;
        private string _latestPL;
        private uint _latestPressure;
        private short _latestTemperature;

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

        // Window span for all charts
        private double _window = 1000;

        // Stretch chart
        public ObservableCollection<ObservablePoint> StretchPoints { get; } = new();
        public ISeries[] StretchSeries { get; }
        public Axis[] StretchXAxes { get; }
        public Axis[] StretchYAxes { get; }

        // Chest chart
        public ObservableCollection<ObservablePoint> ChestRawPoints { get; } = new();
        public ObservableCollection<ObservablePoint> ChestNormPoints { get; } = new();
        public ISeries[] ChestSeries { get; }
        public Axis[] ChestXAxes { get; }
        public Axis[] ChestYAxes { get; }

        // Accelerometer chart (avg)
        public ObservableCollection<ObservablePoint> AccXPoints { get; } = new();
        public ObservableCollection<ObservablePoint> AccYPoints { get; } = new();
        public ObservableCollection<ObservablePoint> AccZPoints { get; } = new();
        public ISeries[] AccSeries { get; }
        public Axis[] AccXAxes { get; }
        public Axis[] AccYAxes { get; }

        // Gyroscope chart (avg)
        public ObservableCollection<ObservablePoint> GyrXPoints { get; } = new();
        public ObservableCollection<ObservablePoint> GyrYPoints { get; } = new();
        public ObservableCollection<ObservablePoint> GyrZPoints { get; } = new();
        public ISeries[] GyrSeries { get; }
        public Axis[] GyrXAxes { get; }
        public Axis[] GyrYAxes { get; }

        // Commands
        public ICommand ScanCommand { get; }
        public ICommand ConnectCommand { get; }
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

        public MainWindowViewModel()
        {
            // BLE service
            _bleService = new BleService();
            _bleService.LogMessage += AppendLog;
            _bleService.DeviceConnected += () => AppendLog("Device connected.");
            _bleService.IMUSamplesReceived += OnIMUSamplesReceived;

            // Battery subscription — updates connected device’s Battery% and optional header value
            _bleService.BatteryLevelUpdated += level =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    BatteryLevel = level;
                    if (SelectedDevice != null)
                        SelectedDevice.BatteryPercent = level;
                });
            };

            // Build stretch chart
            StretchSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = StretchPoints,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColors.LightYellow, 2)
                }
            };
            StretchXAxes = new[] { new Axis { MinLimit = 0, MaxLimit = _window, LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };
            StretchYAxes = new[] { new Axis { MinLimit = 2000, MaxLimit = 5000, LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };

            // Build chest chart
            ChestSeries = new ISeries[]
            {
                new ScatterSeries<ObservablePoint> { Values = ChestRawPoints, GeometrySize = 6, Fill = new SolidColorPaint(SKColors.Red, 1) },
                new LineSeries<ObservablePoint>    { Values = ChestNormPoints, GeometrySize = 0, LineSmoothness = 0, Fill = null, Stroke = new SolidColorPaint(SKColors.Orange, 2) }
            };
            ChestXAxes = new[] { new Axis { MinLimit = 0, MaxLimit = _window, LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };
            ChestYAxes = new[] { new Axis { LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };

            // Build accelerometer chart
            AccSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values       = AccXPoints,
                    GeometrySize = 0,
                    Stroke       = new SolidColorPaint(SKColors.Yellow, 2),
                    Fill         = null
                },
                new LineSeries<ObservablePoint>
                {
                    Values       = AccYPoints,
                    GeometrySize = 0,
                    Stroke       = new SolidColorPaint(SKColors.Blue, 2),
                    Fill         = null
                },
                new LineSeries<ObservablePoint>
                {
                    Values       = AccZPoints,
                    GeometrySize = 0,
                    Stroke       = new SolidColorPaint(SKColors.Red, 2),
                    Fill         = null    }
            };
            AccXAxes = new[] { new Axis { MinLimit = 0, MaxLimit = _window, LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };
            AccYAxes = new[] { new Axis { LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };

            // Build gyroscope chart
            GyrSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values       = GyrXPoints,
                    GeometrySize = 0,
                    Stroke       = new SolidColorPaint(SKColors.Yellow, 2),
                    Fill         = null
                },
                new LineSeries<ObservablePoint>
                {
                    Values       = GyrYPoints,
                    GeometrySize = 0,
                    Stroke       = new SolidColorPaint(SKColors.Blue, 2),
                    Fill         = null
                },
                new LineSeries<ObservablePoint>
                {
                    Values       = GyrZPoints,
                    GeometrySize = 0,
                    Stroke       = new SolidColorPaint(SKColors.Red, 2),
                    Fill         = null
                }
            };
            GyrXAxes = new[] { new Axis { MinLimit = 0, MaxLimit = _window, LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };
            GyrYAxes = new[] { new Axis { LabelsPaint = null, SeparatorsPaint = null, TicksPaint = null } };

            // Subscriptions

            _bleService.StretchDataReceived += (counter, value) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 1) Add & trim to MaxDataPoints
                    StretchPoints.Add(new ObservablePoint(counter, value));
                    if (StretchPoints.Count > MaxDataPoints)
                        StretchPoints.RemoveAt(0);

                    // 2) Autoscale X-axis window
                    if (counter > _window)
                    {
                        StretchXAxes[0].MinLimit = counter - _window;
                        StretchXAxes[0].MaxLimit = counter;
                    }
                    else
                    {
                        StretchXAxes[0].MinLimit = 0;
                        StretchXAxes[0].MaxLimit = _window;
                    }

                    // 3) Notify the chart to re-read the axes
                    OnPropertyChanged(nameof(StretchXAxes));
                    OnPropertyChanged(nameof(StretchYAxes));
                });
            };

            _bleService.ExtendedDataReceived += ext =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // --- 1) Numeric displays ---
                    LatestCounter = ext.TimestampRel;
                    LatestChestRaw = ext.Chest;
                    LatestChestNormalized = ext.ChestNormalized;
                    LatestAccelerometer = string.Join(",", ext.IMU.Select(i => i.AccX));
                    LatestGyroscope = string.Join(",", ext.IMU.Select(i => i.GyrX));
                    LatestPlayerLoad = string.Join(",", ext.PlayerLoad);
                    LatestPressure = ext.Pressure;
                    LatestTemperature = ext.Temperature;

                    // --- 2) Chest plot (raw + processed) ---
                    ChestRawPoints.Add(new ObservablePoint(ext.TimestampRel, ext.Chest));
                    if (ChestRawPoints.Count > MaxDataPoints)
                        ChestRawPoints.RemoveAt(0);

                    ChestNormPoints.Add(new ObservablePoint(ext.TimestampRel, ext.ChestNormalized));
                    if (ChestNormPoints.Count > MaxDataPoints)
                        ChestNormPoints.RemoveAt(0);

                    // autoscale X-axis for chest
                    if (ext.TimestampRel > _window)
                    {
                        ChestXAxes[0].MinLimit = ext.TimestampRel - _window;
                        ChestXAxes[0].MaxLimit = ext.TimestampRel;
                    }
                    else
                    {
                        ChestXAxes[0].MinLimit = 0;
                        ChestXAxes[0].MaxLimit = _window;
                    }
                    OnPropertyChanged(nameof(ChestXAxes));
                    OnPropertyChanged(nameof(ChestYAxes));

                    // --- 3) Accelerometer (avg of 5 samples) ---
                    var avgAx = ext.IMU.Average(i => i.AccX);
                    var avgAy = ext.IMU.Average(i => i.AccY);
                    var avgAz = ext.IMU.Average(i => i.AccZ);

                    AccXPoints.Add(new ObservablePoint(ext.TimestampRel, avgAx));
                    if (AccXPoints.Count > MaxDataPoints)
                        AccXPoints.RemoveAt(0);

                    AccYPoints.Add(new ObservablePoint(ext.TimestampRel, avgAy));
                    if (AccYPoints.Count > MaxDataPoints)
                        AccYPoints.RemoveAt(0);

                    AccZPoints.Add(new ObservablePoint(ext.TimestampRel, avgAz));
                    if (AccZPoints.Count > MaxDataPoints)
                        AccZPoints.RemoveAt(0);

                    // autoscale X-axis for accel
                    if (ext.TimestampRel > _window)
                    {
                        AccXAxes[0].MinLimit = ext.TimestampRel - _window;
                        AccXAxes[0].MaxLimit = ext.TimestampRel;
                    }
                    else
                    {
                        AccXAxes[0].MinLimit = 0;
                        AccXAxes[0].MaxLimit = _window;
                    }
                    OnPropertyChanged(nameof(AccXAxes));
                    OnPropertyChanged(nameof(AccYAxes));

                    // --- 4) Gyroscope (avg of 5 samples) ---
                    var avgGx = ext.IMU.Average(i => i.GyrX);
                    var avgGy = ext.IMU.Average(i => i.GyrY);
                    var avgGz = ext.IMU.Average(i => i.GyrZ);

                    GyrXPoints.Add(new ObservablePoint(ext.TimestampRel, avgGx));
                    if (GyrXPoints.Count > MaxDataPoints)
                        GyrXPoints.RemoveAt(0);

                    GyrYPoints.Add(new ObservablePoint(ext.TimestampRel, avgGy));
                    if (GyrYPoints.Count > MaxDataPoints)
                        GyrYPoints.RemoveAt(0);

                    GyrZPoints.Add(new ObservablePoint(ext.TimestampRel, avgGz));
                    if (GyrZPoints.Count > MaxDataPoints)
                        GyrZPoints.RemoveAt(0);

                    // autoscale X-axis for gyro
                    if (ext.TimestampRel > _window)
                    {
                        GyrXAxes[0].MinLimit = ext.TimestampRel - _window;
                        GyrXAxes[0].MaxLimit = ext.TimestampRel;
                    }
                    else
                    {
                        GyrXAxes[0].MinLimit = 0;
                        GyrXAxes[0].MaxLimit = _window;
                    }
                    OnPropertyChanged(nameof(GyrXAxes));
                    OnPropertyChanged(nameof(GyrYAxes));
                });
            };

            // Commands
            ScanCommand = new RelayCommand(_ => StartScan());
            ConnectCommand = new RelayCommand(async _ => await ConnectToDevice(), _ => SelectedDevice != null);
            SyncRTCCommand = new RelayCommand(async _ => await SyncRTC());
            HardwareInfoCommand = new RelayCommand(async _ => await GetHardwareInfo());
            StartActivityCommand = new RelayCommand(async _ =>
            {
                // Clear plots
                StretchPoints.Clear(); ChestRawPoints.Clear(); ChestNormPoints.Clear();
                AccXPoints.Clear(); AccYPoints.Clear(); AccZPoints.Clear();
                GyrXPoints.Clear(); GyrYPoints.Clear(); GyrZPoints.Clear();
                // Reset axes
                StretchXAxes[0].MinLimit = 0; StretchXAxes[0].MaxLimit = _window;
                ChestXAxes[0].MinLimit = 0; ChestXAxes[0].MaxLimit = _window;
                AccXAxes[0].MinLimit = 0; AccXAxes[0].MaxLimit = _window;
                GyrXAxes[0].MinLimit = 0; GyrXAxes[0].MaxLimit = _window;
                await StartActivity();
            }, _ => SelectedDevice != null);
            StopActivityCommand = new RelayCommand(async _ => await StopActivity());
            ListFilesCommand = new RelayCommand(async _ => await ListFiles());
            DownloadAllCommand = new RelayCommand(async _ => await DownloadAllFiles());
            StartStretchCommand = new RelayCommand(async _ => await StartStretchStream(), _ => SelectedDevice != null);
            StopStretchCommand = new RelayCommand(async _ => await StopStretchStream());
            EraseFilesCommand = new RelayCommand(async _ => await EraseFiles());
            FactoryCalCommand = new RelayCommand(async _ => await DoFactoryCal());
        }

        // --- Filter-aware logging ---
        private void AppendLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _allLogs.Add(line);

            // Only append to visible log if current filters allow it
            if (IsAllowed(message))
                Log += line + "\n";
        }

        // Decide if a message passes filters based on emoji/prefix used in logs
        private bool IsAllowed(string msg)
        {
            if (msg.StartsWith("🔋")) return ShowBattery;
            if (msg.StartsWith("🤖")) return ShowIMU;
            if (msg.StartsWith("🌬️")) return ShowBreathing;
            if (msg.StartsWith("🏋️")) return ShowStretch;
            if (msg.StartsWith("🌡️")) return ShowPressureTemp;
            if (msg.StartsWith("❤️‍🩹") || msg.StartsWith("❤️")) return ShowHR;

            return ShowGeneral; // discovery, connect, files, scan, generic info
        }

        private void RebuildLogView()
        {
            var sb = new StringBuilder();
            foreach (var line in _allLogs)
            {
                // find "] " and grab everything after it without using the range operator
                int idx = line.IndexOf("] ");
                string msg = (idx >= 0 && (idx + 2) < line.Length)
                    ? line.Substring(idx + 2)
                    : line;

                if (IsAllowed(msg)) sb.AppendLine(line);
            }
            Log = sb.ToString();
        }

        // BLE control methods
        public void StartScan() => _bleService.StartScanning();

        public async Task ConnectToDevice()
        {
            if (SelectedDevice == null) return;
            bool ok = await _bleService.ConnectToDeviceAsync(SelectedDevice);
            AppendLog(ok ? "Connected successfully." : "Failed to connect.");
        }

        public async Task SyncRTC()
        {
            await _bleService.SyncRTCAsync();
            AppendLog("RTC synced.");
        }

        public async Task GetHardwareInfo()
        {
            var data = await _bleService.SendControlCommandAsync(0x0001);
            var info = HardwareInfo.Parse(data);
            AppendLog("Device Info:\n" + info);
        }

        public async Task StartActivity()
        {
            await _bleService.StartActivityAsync();
            AppendLog("Start Activity command sent.");
        }

        public async Task StopActivity()
        {
            await _bleService.StopActivityAsync();
            AppendLog("Stop Activity command sent.");
        }

        public async Task ListFiles()
        {
            await _bleService.ListFilesAsync();
            var files = _bleService.FileList;
            AppendLog($"📂 {files.Count} file(s)");
            foreach (var f in files)
                AppendLog(f.ToString());
        }

        public async Task DownloadAllFiles()
        {
            foreach (var file in _bleService.FileList)
                await _bleService.DownloadFileAsync(file);

            AppendLog("All files downloaded.");
        }

        public async Task StartStretchStream()
        {
            await _bleService.StartStretchStreamAsync(SelectedDevice.Name);
        }

        public async Task StopStretchStream()
        {
            await _bleService.StopStretchStreamAsync();
        }

        public async Task EraseFiles()
        {
            await _bleService.EraseFilesAsync();
        }

        private async Task DoFactoryCal()
        {
            AppendLog("🔧 Sending factory calibration command…");
            try
            {
                await _bleService.FactoryCalibrationAsync();
                AppendLog("✅ Factory calibration completed.");
                await Task.Delay(5000);
                AppendLog("🎉 Factory calibration successful.");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Factory calibration failed: {ex.Message}");
            }
        }

        private void OnIMUSamplesReceived(List<IMUSample> samples)
        {
            foreach (var sample in samples)
            {
                // Convert raw int16 to float (assuming ±32768 maps to ±16g for accel and ±2000 dps for gyro)
                float ax = sample.AccX / 32768f * 16f;
                float ay = sample.AccY / 32768f * 16f;
                float az = sample.AccZ / 32768f * 16f;

                float gx = sample.GyrX / 32768f * 2000f;
                float gy = sample.GyrY / 32768f * 2000f;
                float gz = sample.GyrZ / 32768f * 2000f;

                // Convert degrees/sec to rad/sec
                float degToRad = (float)(Math.PI / 180);
                gx *= degToRad;
                gy *= degToRad;
                gz *= degToRad;

                // Update Madgwick filter
                _madgwick.Update(gx, gy, gz, ax, ay, az);

                // Get orientation
                var (w, x, y, z) = _madgwick.Quaternion;
                Orientation = new System.Windows.Media.Media3D.Quaternion(x, y, z, w);  // WPF expects xyzw
            }
        }
    }
}