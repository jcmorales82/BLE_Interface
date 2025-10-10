using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace BLE_Interface.Services
{
    /// <summary>
    /// Writes processed data (breathing samples, timestamps, and IMU data) to a timestamped JSON file under the user's Downloads folder.
    /// Format: "Downloads/<DeviceName>/YYYY-MM-DD_HH-MM-SS/processed_data.json"
    /// Structure: {"samples":[{"ts":epochMs,"s_id":counter,"rbr":rawBR,"pbr":processedBR,"rvt":rawTV,"pvt":processedTV,"rve":rawMV,"pve":processedMV}],"breathTimestamps":[{"ts":epochMs,"s_id":counter,"vts":valleyTime,"vv":valleyValue,"pts":peakTime,"pv":peakValue}],"imu":[{"ts":epochMs,"s_id":counter,"cadence":cadence,"st":stepTime,"pl":playerLoad}]}
    /// </summary>
    public sealed class ProcessedDataLogger : IDisposable
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private readonly List<object> _samples = new List<object>();
        private readonly List<object> _timestamps = new List<object>();
        private readonly List<object> _imuData = new List<object>();
        private bool _disposed = false;

        public string FilePath => _filePath;

        public ProcessedDataLogger(string deviceDisplayName, string customFolderPath = null)
        {
            var downloadsFolder = customFolderPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // Create device folder
            var safeDeviceName = Sanitize(deviceDisplayName);
            var deviceFolder = Path.Combine(downloadsFolder, safeDeviceName);
            Directory.CreateDirectory(deviceFolder);

            // Create timestamp folder inside device folder
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var timestampFolder = Path.Combine(deviceFolder, timestamp);
            Directory.CreateDirectory(timestampFolder);

            var fileName = "processed_data.json";
            _filePath = Path.Combine(timestampFolder, fileName);

            // Initialize empty JSON file - we'll write the complete JSON when disposing
            File.WriteAllText(_filePath, "", Encoding.UTF8);
        }

        public ProcessedDataLogger(string deviceDisplayName, string customFolderPath, string timestampFolder)
        {
            var downloadsFolder = customFolderPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // Create device folder
            var safeDeviceName = Sanitize(deviceDisplayName);
            var deviceFolder = Path.Combine(downloadsFolder, safeDeviceName);
            Directory.CreateDirectory(deviceFolder);

            // Use provided timestamp folder
            var fullTimestampFolder = Path.Combine(deviceFolder, timestampFolder);
            Directory.CreateDirectory(fullTimestampFolder);

            var fileName = "processed_data.json";
            _filePath = Path.Combine(fullTimestampFolder, fileName);

            // Initialize empty JSON file - we'll write the complete JSON when disposing
            File.WriteAllText(_filePath, "", Encoding.UTF8);
        }

        public void AppendSample(BleService.BreathDataPoint breathData)
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    // Get current system timestamp in milliseconds since epoch
                    long epochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // Create sample object matching the specified format
                    // Note: For JSON, we save the raw device values (multiply by 10 to get original values)
                    var sample = new
                    {
                        ts = epochMs,
                        s_id = breathData.Counter,
                        rbr = breathData.RawBRInterval * 10,      // Convert back to raw device value
                        pbr = breathData.ProcessedBRInterval * 10, // Convert back to raw device value
                        rvt = breathData.RawTidalVolume,
                        pvt = breathData.ProcessedTidalVolume,
                        rve = breathData.RawMinuteVolume,
                        pve = breathData.ProcessedMinuteVolume
                    };

                    _samples.Add(sample);
                }
                catch (Exception ex)
                {
                    // Log error but don't throw to avoid breaking data flow
                    System.Diagnostics.Debug.WriteLine($"BreathingDataLogger sample error: {ex.Message}");
                }
            }
        }

        public void AppendTimestamp(BleService.BreathTimestampsDataPoint timestampData)
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    // Get current system timestamp in milliseconds since epoch
                    long epochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // Create timestamp object matching the specified format
                    var timestamp = new
                    {
                        ts = epochMs,
                        s_id = timestampData.Counter,
                        vts = timestampData.TvValleyTimeIndex,
                        vv = timestampData.TvValleyValue,
                        pts = timestampData.TvPeakTimeIndex,
                        pv = timestampData.TvPeakValue
                    };

                    _timestamps.Add(timestamp);
                }
                catch (Exception ex)
                {
                    // Log error but don't throw to avoid breaking data flow
                    System.Diagnostics.Debug.WriteLine($"BreathingDataLogger timestamp error: {ex.Message}");
                }
            }
        }

        public void AppendImuData(uint counter, ushort cadence, uint stepTime, ushort playerLoad)
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    // Get current system timestamp in milliseconds since epoch
                    long epochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // Create IMU data object matching the specified format
                    var imuData = new
                    {
                        ts = epochMs,
                        s_id = counter,
                        cadence = cadence,
                        st = stepTime,
                        pl = playerLoad
                    };

                    _imuData.Add(imuData);
                }
                catch (Exception ex)
                {
                    // Log error but don't throw to avoid breaking data flow
                    System.Diagnostics.Debug.WriteLine($"ProcessedDataLogger IMU error: {ex.Message}");
                }
            }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                lock (_lock)
                {
                    try
                    {
                        // Create the complete JSON object with samples, breathTimestamps, and imu arrays
                        var jsonObject = new
                        {
                            samples = _samples,
                            breathTimestamps = _timestamps,
                            imu = _imuData
                        };

                        // Serialize to JSON and write the complete file
                        var json = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions
                        {
                            WriteIndented = false
                        });

                        File.WriteAllText(_filePath, json, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"BreathingDataLogger dispose error: {ex.Message}");
                    }
                }
            }
        }
    }
}
