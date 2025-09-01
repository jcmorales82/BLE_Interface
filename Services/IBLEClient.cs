using System;
using System.Threading;
using System.Threading.Tasks;

namespace BLE_Interface.Services
{
    public interface IBLEClient : IDisposable
    {
        event Action<DiscoveredDevice> DeviceFound;
        Task StartScanAsync(Guid? filterServiceUuid = null, CancellationToken ct = default);
        Task StopScanAsync();
        Task ConnectAsync(ulong bluetoothAddress, CancellationToken ct = default);
        Task DisconnectAsync();
        Task SubscribeAsync(Guid characteristicId, Func<byte[], Task> onData, CancellationToken ct = default);
        Task UnsubscribeAsync(Guid characteristicId);
        bool IsConnected { get; }
    }

    public sealed class DiscoveredDevice
    {
        public ulong Address { get; }
        public string Name { get; }
        public short Rssi { get; }
        public DateTimeOffset LastSeen { get; }

        public DiscoveredDevice(ulong address, string name, short rssi, DateTimeOffset lastSeen)
        {
            Address = address;
            Name = name;
            Rssi = rssi;
            LastSeen = lastSeen;
        }

        public override string ToString()
            => $"{Name} [{Address}] RSSI={Rssi} LastSeen={LastSeen:O}";
    }
}