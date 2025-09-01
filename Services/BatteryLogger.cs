// AI-SECTION BatteryLogger
// Drop this file under: Services/BatteryLogger.cs
using System;
using System.IO;
using System.Text;

namespace BLE_Interface.Services
{
    /// <summary>
    /// Writes battery samples to a timestamped CSV file under the user's Downloads folder.
    /// Format: "MM_dd_yy_HH_mm_<DeviceName>_BatteryProfile.csv"
    /// Columns: Timestamp,Charge,Battery%
    /// </summary>
    public sealed class BatteryLogger
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        public string FilePath => _filePath;

        public BatteryLogger(string deviceDisplayName, string downloadsFolder = null)
        {
            downloadsFolder = downloadsFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            Directory.CreateDirectory(downloadsFolder);

            var safeName = Sanitize(deviceDisplayName);
            var fileName = $"{DateTime.Now:MM_dd_yy_HH_mm}_{safeName}_BatteryProfile.csv";
            _filePath = Path.Combine(downloadsFolder, fileName);

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
