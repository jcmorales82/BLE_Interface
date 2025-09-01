using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BLE_Interface.Services
{
    /// <summary>
    /// Encapsulates subscription and reads for the standard GATT Battery Service (0x180F).
    /// </summary>
    public sealed class BatteryServiceSession : IDisposable
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

                // Initial read
                var val = await ReadAsync();
                if (val.HasValue) BatteryLevelChanged?.Invoke(val.Value);
            }
            catch
            {
                // ignore; device may not support notify
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

    public static class BatteryServiceHelper
    {
        public static Task<BatteryServiceSession> AttachAsync(BluetoothLEDevice dev, Action<byte> onLevelUpdated)
            => BatteryServiceSession.CreateAsync(dev, onLevelUpdated);
    }
}
