using System;

namespace BLE_Interface.Services.Bluetooth
{
    public static class BleConstants
    {
        // Tyme Wear custom service UUID
        public static readonly Guid TymewearServiceUuid =
            new Guid("40B50000-30B5-11E5-A151-FEFF819CDC90");

        // Standard Battery Service / Characteristic
        public static readonly Guid BatteryServiceUuid =
            new Guid("0000180F-0000-1000-8000-00805F9B34FB");
        public static readonly Guid BatteryLevelCharacteristicUuid =
            new Guid("00002A19-0000-1000-8000-00805F9B34FB");
    }
}
