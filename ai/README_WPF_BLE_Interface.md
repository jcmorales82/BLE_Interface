# WPF BLE Interface

_Protocol‑synced README — regenerated 2025-08-12 16:14 UTC_

This document merges the **firmware protocol** with the **desktop app** so you can see at a glance what’s supported and what’s next. It includes only app‑level symbols from firmware (`BLE_REQ_*`, `BLE_STAT_*`, `BLE_RESP_*`) — Nordic SoftDevice constants are intentionally omitted.

---

## GATT Profile (Custom Service)
- **Service UUID:** 40B50000-30B5-11E5-A151-FEFF819CDC90
- **Data Stream Characteristic (Notify):** 40B50004-30B5-11E5-A151-FEFF819CDC90
- **Download Characteristic (Notify):** 40B50001-30B5-11E5-A151-FEFF819CDC90
- **Control Characteristic (Notify/Write):** 40B50007-30B5-11E5-A151-FEFF819CDC90

> The WPF app filters advertisements by the custom service UUID, connects, discovers the 3 characteristics, and enables **Notify** on each.

---

## Control Commands (Requests)

| Opcode | Name | Status |
|---|---|---|
| 0x0001 | BLE_REQ_GET_INFO | implemented |
| 0x0002 | BLE_REQ_GET_INFO_SHORT | not implemented |
| 0x0020 | BLE_REQ_START_ACTIVITY | implemented |
| 0x0021 | BLE_REQ_STOP_ACTIVITY | implemented |
| 0x002B | BLE_REQ_ERASE_ALL_FILES | implemented |
| 0x002C | BLE_REQ_SET_TIMESTAMP | implemented |
| 0x002D | BLE_REQ_START_STRETCH_DATA | implemented |
| 0x002E | BLE_REQ_STOP_STRETCH_DATA | implemented |
| 0x0040 | BLE_REQ_FORMAT_FILESYSTEM | not implemented |
| 0x00F0 | BLE_REQ_REBOOT | not implemented |
| 0x0108 | BLE_REQ_DATA_DUMP | implemented |
| 0x0109 | BLE_REQ_LIST_FILES | implemented |
| 0x010A | BLE_REQ_FACTORY_CALIBRATION | implemented |
| 0x010B | BLE_REQ_START_USER_CALIBRATION | not implemented |
| 0x010C | BLE_REQ_STOP_USER_CALIBRATION | not implemented |
| 0x010D | BLE_REQ_SET_DEV_NAME | not implemented |
| 0x010E | BLE_REQ_SET_USER_CALIBRATION | not implemented |
| 0x0110 | BLE_REQ_START_FITTING_UPDATES | not implemented |
| 0x0111 | BLE_REQ_STOP_FITTING_UPDATES | not implemented |
| 0x0112 | BLE_REQ_SET_HRM_NAME | not implemented |
| 0x0113 | BLE_REQ_GET_HRM_NAME | not implemented |

> **implemented** = currently invoked by the WPF app (`SendControlCommandAsync(...)`) or confirmed in-app usage.

---

## Status Events (selected)
- **0x4002 — Battery**: parsed and appended to `Downloads/battery.csv` by the WPF app.
- (See firmware repo for the full list of `BLE_STAT_*` codes.)

---

## Response Codes
The app logs and maps `BLE_RESP_*` to human‑readable messages for debugging. (See firmware repo for the full list.)

---

## Data Stream Types
_Data types emitted on the **Data Stream** characteristic (first byte = type):_

| Type | Name (summary) | Parsed by app |
|---|---|---|
| 0 | **Extended sample**: counter, chest raw/normalized, 5× IMU (acc/gyr), 5× playerLoad, pressure, temperature | Yes |
| 1 | **Breathing (processed)**: timestamp, BR/TV/MV (raw & processed) | Yes |
| 2 | **IMU processed**: timestamp, cadence, stepTime, playerLoad | Yes |
| 3 | **Stretch (raw)**: counter, stretch | Yes |
| 4 | **Pressure & Temperature**: counter, pressure (Pa×100), temp (°C×100) | Yes |
| 5 | **Heart Rate**: timestamp, HR | Yes |

The app plots **Stretch**, **Chest (raw + filtered)**, **Accelerometer (X/Y/Z)**, **Gyroscope (X/Y/Z)** (LiveCharts2), and computes a real‑time **3D orientation** (HelixToolkit) using Madgwick AHRS on IMU data.

---

## File Operations
- **LIST_FILES (0x0109)** returns entries of:
  ```c
  typedef struct {
      uint32_t timestamp;
      uint32_t startAddr;
      uint32_t endAddr;
  } fileEntry_t;
  ```
- **DATA_DUMP (0x0108)** streams typed records (breathing, IMU, HR, etc.). The desktop decoder writes them into a readable TXT with sections `[Breathing]`, `[IMU]`, `[HR]` and closes the file after **1 s** of inactivity (reset on each packet).

_Output location:_ `Downloads/` (decoded TXT, optional raw stretch dump, `battery.csv`).

---

## GET_INFO Payload (from firmware)

```c
typedef struct
{
    uint16_t swVersion;
    uint8_t  hwVersion;
    uint16_t imuPeriod;
    uint16_t dataPointPeriod;
    uint8_t  hwStatus;
    uint16_t baseCalibration;
    uint16_t userVtCalibration;
    uint32_t currentRTC;
    uint32_t activityStartTimestamp;
    uint16_t activityThreshold;
} getInfo_t;
```

---

## Battery Service (Standard GATT) — Planned
**Service UUID:** 0x180F (Battery Service)  
**Characteristic:** 0x2A19 (Battery Level, uint8 0–100%)

The firmware already emits battery via a status event (e.g., **0x4002**). Adding the **standard GATT Battery Service** enables OS‑level battery reads and a simple UI binding in the app.

**Plan**
1. Discover Battery Service (0x180F) after connecting.  
2. Get Battery Level (0x2A19).  
3. Read initial value; enable notifications if supported.  
4. Bind a `BatteryLevel` property in `MainWindowViewModel`.  
5. (Optional) Append to `Downloads/battery.csv` with `source=standard_gatt` to distinguish from the status event.

**Sample code (C#)**
```csharp
// After you've connected and have a BluetoothLEDevice 'dev'
var battSvc = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
if (battSvc.Status == GattCommunicationStatus.Success && battSvc.Services.Count > 0)
{
    var svc = battSvc.Services[0];
    var chars = await svc.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
    if (chars.Status == GattCommunicationStatus.Success && chars.Characteristics.Count > 0)
    {
        var battChar = chars.Characteristics[0];

        // Read initial
        var read = await battChar.ReadValueAsync();
        if (read.Status == GattCommunicationStatus.Success)
        {
            var reader = DataReader.FromBuffer(read.Value);
            byte level = reader.ReadByte(); // 0–100
            ViewModel.BatteryLevel = level;
        }

        // Subscribe
        battChar.ValueChanged += (s, e) =>
        {
            var dr = DataReader.FromBuffer(e.CharacteristicValue);
            byte level = dr.ReadByte();
            ViewModel.BatteryLevel = level;
            // Optional: append to Downloads/battery.csv with "standard_gatt" source tag
        };

        await battChar.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
    }
}
```

**UI tip:** show a small battery icon + percentage near the connection status. If both sources are present (status event and GATT 0x2A19), prefer **0x2A19** for display and keep the status event for logging/diagnostics.
