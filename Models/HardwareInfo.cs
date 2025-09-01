using System;
using System.Runtime.InteropServices;

namespace BLE_Interface.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HardwareInfo
    {
        public ushort swVersion;
        public byte hwVersion;
        public ushort imuPeriod;
        public ushort dataPointPeriod;
        public byte hwStatus;
        public ushort baseCalibration;
        public ushort userVtCalibration;
        public uint currentRTC;
        public uint activityStartTimestamp;
        public ushort activityThreshold;

        public static HardwareInfo Parse(byte[] data)
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { return Marshal.PtrToStructure<HardwareInfo>(handle.AddrOfPinnedObject()); }
            finally { handle.Free(); }
        }

        public override string ToString()
        {
            DateTime rtc = DateTimeOffset.FromUnixTimeSeconds(currentRTC).LocalDateTime;
            DateTime activityStart = DateTimeOffset.FromUnixTimeSeconds(activityStartTimestamp).LocalDateTime;
            string version = $"0.{swVersion & 0xFF:X2}";
            string[] hwFlags = new[]
            {
                "Mode:\t\t" + ((hwStatus & (1 << 0)) != 0 ? "Extended" : "Normal"),
                "Activities:\t" + ((hwStatus & (1 << 6)) != 0 ? "Available" : "None"),
                "Stretch Sensor:\t" + ((hwStatus & (1 << 1)) != 0 ? "✅" : "❌"),
                "Calibration:\t" + ((hwStatus & (1 << 2)) != 0 ? "✅" : "❌"),
                "IMU Sensors:\t" + ((hwStatus & (1 << 4)) != 0 ? "✅" : "❌"),
                "Altitude Sensor:\t" + ((hwStatus & (1 << 5)) != 0 ? "✅" : "❌")
            };

            return
                $"SW Version: {version}\n" +
                $"HW Version: {hwVersion}\n" +
                $"IMU Period: {imuPeriod} ms\n" +
                $"DataPoint Period: {dataPointPeriod} ms\n" +
                $"Hardware Status:\n  - {string.Join("\n  - ", hwFlags)}\n" +
                $"Base Calibration: {baseCalibration}\n" +
                $"Hardware RTC: {rtc:G}\n" +
                $"Last Activity Timestamp: {activityStart:G}\n" +
                $"Activity Threshold: {activityThreshold}";
        }
    }
}
