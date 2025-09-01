using System;
using System.Runtime.InteropServices;

namespace BLE_Interface.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FileEntry
    {
        public uint timestamp;
        public uint startAddr;
        public uint endAddr;
        public uint Size => endAddr - startAddr;
        public string GetFilename() => $"file_{timestamp}.bin";
        public override string ToString() => $"{timestamp} — {Size} bytes";
    }
}
