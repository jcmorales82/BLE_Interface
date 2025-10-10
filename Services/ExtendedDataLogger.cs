using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace BLE_Interface.Services
{
    /// <summary>
    /// Writes extended data samples to a timestamped JSON file under the user's Downloads folder.
    /// Format: "Downloads/<DeviceName>/YYYY-MM-DD_HH-MM-SS/extended_data.json"
    /// Structure: {"samples":[{"id":timestamp,"c":chestNorm,"cr":chestRaw,"alt":altitude,"t":temp,"p":pressure,"ax":[5values],"ay":[5values],"az":[5values],"gx":[5values],"gy":[5values],"gz":[5values],"pl":[5values]}]}
    /// </summary>
    public sealed class ExtendedDataLogger : IDisposable
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private readonly List<object> _samples = new List<object>();
        private bool _disposed = false;
        private bool _jsonStarted = false;

        public string FilePath => _filePath;

        public ExtendedDataLogger(string deviceDisplayName, string downloadsFolder = null)
        {
            downloadsFolder = downloadsFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // Create device folder
            var safeDeviceName = Sanitize(deviceDisplayName);
            var deviceFolder = Path.Combine(downloadsFolder, safeDeviceName);
            Directory.CreateDirectory(deviceFolder);

            // Create timestamp folder inside device folder
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var timestampFolder = Path.Combine(deviceFolder, timestamp);
            Directory.CreateDirectory(timestampFolder);

            var fileName = "extended_data.json";
            _filePath = Path.Combine(timestampFolder, fileName);

            // Initialize JSON file with opening structure
            File.WriteAllText(_filePath, "{\"samples\":[", Encoding.UTF8);
        }

        public ExtendedDataLogger(string deviceDisplayName, string downloadsFolder, string timestampFolder)
        {
            downloadsFolder = downloadsFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // Create device folder
            var safeDeviceName = Sanitize(deviceDisplayName);
            var deviceFolder = Path.Combine(downloadsFolder, safeDeviceName);
            Directory.CreateDirectory(deviceFolder);

            // Use provided timestamp folder
            var fullTimestampFolder = Path.Combine(deviceFolder, timestampFolder);
            Directory.CreateDirectory(fullTimestampFolder);

            var fileName = "extended_data.json";
            _filePath = Path.Combine(fullTimestampFolder, fileName);

            // Initialize JSON file with opening structure
            File.WriteAllText(_filePath, "{\"samples\":[", Encoding.UTF8);
        }

        public void Append(BleService.ExtendedDataPoint data)
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    // Calculate altitude from pressure and temperature
                    double altitude = CalculateAltitude(data.Pressure, data.Temperature);

                    // Extract IMU data arrays
                    var accX = data.IMU.Select(s => s.AccX).ToArray();
                    var accY = data.IMU.Select(s => s.AccY).ToArray();
                    var accZ = data.IMU.Select(s => s.AccZ).ToArray();
                    var gyrX = data.IMU.Select(s => s.GyrX).ToArray();
                    var gyrY = data.IMU.Select(s => s.GyrY).ToArray();
                    var gyrZ = data.IMU.Select(s => s.GyrZ).ToArray();

                    // Create sample object with original readable field order
                    var sample = new
                    {
                        id = data.TimestampRel,
                        c = data.ChestNormalized,
                        cr = data.Chest,
                        alt = Math.Round(altitude, 1),
                        t = data.Temperature,
                        p = data.Pressure,
                        ax = accX,
                        ay = accY,
                        az = accZ,
                        gx = gyrX,
                        gy = gyrY,
                        gz = gyrZ,
                        pl = data.PlayerLoad.ToArray()
                    };

                    // Add comma before new sample if not the first one
                    string jsonSample = JsonSerializer.Serialize(sample, new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });

                    if (_jsonStarted)
                    {
                        File.AppendAllText(_filePath, "," + jsonSample, Encoding.UTF8);
                    }
                    else
                    {
                        File.AppendAllText(_filePath, jsonSample, Encoding.UTF8);
                        _jsonStarted = true;
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't throw to avoid breaking data flow
                    System.Diagnostics.Debug.WriteLine($"ExtendedDataLogger error: {ex.Message}");
                }
            }
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
                
                // Close the JSON array and object properly
                File.AppendAllText(_filePath, "]}", Encoding.UTF8);
            }
        }
    }
}
