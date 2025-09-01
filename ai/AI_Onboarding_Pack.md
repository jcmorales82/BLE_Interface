# WPF BLE Interface — AI Onboarding Pack

_Last updated: 2025-08-12 14:57 UTC_

## What this app is
A Windows **WPF (.NET Framework 4.8)** desktop app that scans for a custom BLE device, connects, exchanges control commands, **streams live sensor data**, **downloads stored files**, and **plots/visualizes** metrics in real time (including a 3D orientation cube).

---

## Repo layout (key files)
- `Views/MainWindow.xaml` + `Views/MainWindow.xaml.cs` — UI and window glue.
- `ViewModels/MainWindowViewModel.cs` — MVVM binding layer, commands, charts, and orientation.
- `Services/BleService.cs` — **All BLE**: scan, connect, GATT discovery, opcodes, streaming decode, file download.
- `Models/*` — data transfer objects (`BleDeviceModel`, `FileEntry`, `HardwareInfo`, `IMUSample`).
- `SensorFusion/MadgwickAHRS.cs` — fusion filter for IMU → quaternion.
- `Helpers/OrientationVisualizer.cs` — binds the quaternion to the HelixToolkit 3D cube.
- `BLE_Interface.csproj` — target framework, packages.

---

## Build & run
- **Target Framework:** `net48`
- **Language Version:** C# 10
- **Packages:**
- **HelixToolkit.Wpf** 2.27.0
- **LiveChartsCore** 2.0.0-rc5.4
- **LiveChartsCore.SkiaSharpView** 2.0.0-rc5.4
- **LiveChartsCore.SkiaSharpView.WPF** 2.0.0-rc5.4
- **Microsoft.Windows.SDK.Contracts** 10.0.26100.3916

**Requirements:** Windows 10/11 with Bluetooth, Visual Studio 2022, and permissions to use Bluetooth. Restore NuGet packages and press **F5**. No UWP packaging required — WinRT BLE APIs are consumed from WPF via `Microsoft.Windows.SDK.Contracts`.

---

## BLE profile used by the app
- **Custom Service UUID:** `40B50000-30B5-11E5-A151-FEFF819CDC90`
- **Characteristics (notify + roles):**
  1. **Data Stream** — `40B50004-30B5-11E5-A151-FEFF819CDC90` (Notify)
  2. **Control** — `40B50007-30B5-11E5-A151-FEFF819CDC90` (Notify, Write)
  3. **Download** — `40B50001-30B5-11E5-A151-FEFF819CDC90` (Notify)

**Discovery flow:** filter advertisements by the custom service UUID, connect, get service by UUID, then fetch the 3 characteristics and enable **Notify** on each.

**Device naming in UI:** devices are shown as `TYME-XXXX` derived from the last 4 hex digits of the MAC; RSSI is updated live.

---

## Control opcodes & statuses
### Opcodes sent by the app
| Opcode | Meaning |
|---:|---|
| 0x0001 | GET_INFO |
| 0x0020 | START_ACTIVITY |
| 0x0021 | STOP_ACTIVITY |
| 0x002B | ERASE_FILES |
| 0x002C | SYNC_RTC |
| 0x002D | START_STRETCH |
| 0x002E | STOP_STRETCH |
| 0x0108 | DATA_DUMP |
| 0x0109 | LIST_FILES |
| 0x010A | FACTORY_CALIBRATION |

### Response/Status codes parsed
| Code | Meaning |
|---:|---|
| 0x4000 | STAT_NO_MEM |
| 0x4001 | STAT_FITTING |
| 0x8000 | SUCCESS |
| 0x8100 | INVALID_REQ |
| 0x8200 | INVALID_PARAM |
| 0x8300 | NOT_FOUND |
| 0x8400 | ERROR |
| 0x8500 | BUSY |
| 0x8600 | LOCKED |
| 0x8700 | FORBIDDEN |
| 0x8800 | NO_MEM |

- Special status **0x4002**: parsed as Battery status and appended to `Downloads/battery.csv`.

---

## Live streaming data (Data Stream characteristic)
The app parses multiple **dataType** payloads (first byte = type):

- **Type 0 – Extended datapoint** (total 85 bytes):  
  `counter (u32), chest raw (u16), chest normalized (u16), 5× IMU samples (accX/Y/Z + gyrX/Y/Z), 5× playerLoad (u16), pressure (u32, Pa×100), temperature (i16, °C×100)`.  
  Routed to `ExtendedDataReceived` → ViewModel updates **numeric readouts** and appends to **Chest/Acc/Gyr charts**.
- **Type 1 – Breathing (processed)**: `timestamp, rawBR, procBR, rawTV, procTV, rawMV, procMV` → logged and written when downloading.
- **Type 2 – IMU processed**: `timestamp, cadence (u16), stepTime (u32), playerLoad (u16)` → logged.
- **Type 3 – Stretch (raw)**: `counter (u32), stretch (u16)` → plotted live and optionally streamed to `Downloads/<device>_<epoch>_raw.txt`.
- **Type 4 – Pressure/Temperature**: `counter (u32), pressure (u32, Pa×100), temp (i16, °C×100)` → numeric readouts.
- **Type 5 – Heart Rate**: `timestamp (u32), heartRate (u16)` → numeric/log.

---

## File management (Download characteristic)
- **List files** (`0x0109`) → payload is a sequence of:
  ```c
  struct FileEntry { uint32 timestamp; uint32 startAddr; uint32 endAddr; }
  ```
- **Download file** (`0x0108`, param = `timestamp`) → stream of typed records decoded into a readable **TXT** with sections `[Breathing]`, `[IMU]`, `[HR]`.  
  File is auto-closed after **1s of inactivity** (reset on each packet).

Outputs are written to the local **`Downloads/`** folder:
- `file_<timestamp>.txt` (decoded download)
- `<device>_<epoch>_raw.txt` (live stretch dump)
- `battery.csv` (`seconds,chargeValue` from status `0x4002`)

---

## UI, charts, and 3D
- **MVVM**: `MainWindowViewModel` exposes bindable properties + `RelayCommand` for actions.
- **Charts** (LiveCharts2/Skia): real-time **Stretch**, **Chest (raw + filtered)**, **Accelerometer (X/Y/Z)**, **Gyroscope (X/Y/Z)** with windowed X‑axis.
- **3D Orientation** (HelixToolkit): a cube rotated by a quaternion from **MadgwickAHRS** (`sampleRate ≈ 125 Hz`) computed from **averaged IMU** samples.

---

## Typical flows
1. **Scan → Connect**  
   Click **Scan**, select `TYME-XXXX`, then **Connect**.
2. **Sync clock** (optional)  
   **Sync RTC** (`0x002C`) to align device time.
3. **Live session**  
   **Start Activity** (`0x0020`) to begin streaming; watch plots and 3D cube; **Stop Activity** (`0x0021`) to end.
4. **Download stored data**  
   **List Files** → **Download All** (loops over entries and decodes into TXT files).
5. **Utilities**  
   **Start/Stop Stretch** (live raw dump), **Erase Files** (`0x002B`), **Factory Cal** (`0x010A`).

---

## Extension points & notes
- **Device name**: UI uses a MAC‑derived fallback. If desired, add parsing of **Advertisement Local Name** or read GATT **0x1800/0x2A00** after connect.
- **Progress UI**: Add a progress bar for file downloads (based on `endAddr - startAddr` vs bytes decoded).
- **Resilience**: Consider reconnect/backoff and exception handling if services/characteristics are unavailable.
- **Data export**: Stream decoded live values to CSV in addition to file downloads.
- **Unit scaling**: ViewModel currently assumes ±16g and ±2000 dps ranges; adjust if device uses different full-scales.

---

## Where to look in code (quick map)
- **Scanning & filtering**: `Services/BleService.cs` (`BluetoothLEAdvertisementWatcher`, custom service UUID filter)
- **Connect & GATT**: `BleService.ConnectToDeviceAsync`, `FetchCharAsync`, `EnableNotifyAsync`
- **Control requests**: `BleService.SendControlCommandAsync` (tag-based matching, logging via `opcodeNames`/`responseCodes`)
- **Streaming decode**: `BleService.DataStreamCharacteristic_ValueChanged` (`switch(type)` with cases 0–5)
- **Downloads**: `BleService.DownloadCharacteristic_ValueChanged`, `StartInactivityMonitor`
- **UI bindings**: `ViewModels/MainWindowViewModel.cs` (commands + chart series)
- **3D**: `Helpers/OrientationVisualizer.cs`, `Views/MainWindow.xaml` (HelixToolkit cube)

---

## Short feature list (what the code does)
- Scans for BLE devices advertising the custom service and shows **TYME‑XXXX** with live **RSSI**.
- Connects, discovers 3 **characteristics**, and enables **Notify**.
- Implements control opcodes: **GET_INFO**, **START/STOP_ACTIVITY**, **SYNC_RTC**, **LIST_FILES**, **DATA_DUMP**, **START/STOP_STRETCH**, **ERASE_FILES**, **FACTORY_CAL**.
- Parses and visualizes **live data** (breathing, stretch, IMU, HR, pressure/temp); computes a **real-time 3D orientation** from IMU via **Madgwick**.
- Downloads and **decodes stored files** into friendly TXT sections; auto-finishes via **inactivity timer**.
- Writes additional **battery telemetry** to `Downloads/battery.csv` from status `0x4002`.
- Provides a clean **MVVM** WPF UI with LiveCharts2 and HelixToolkit.

---

### GATT UUIDs
- Service: `40B50000-30B5-11E5-A151-FEFF819CDC90`
- Data Stream Char: `40B50004-30B5-11E5-A151-FEFF819CDC90`
- Control Char: `40B50007-30B5-11E5-A151-FEFF819CDC90`
- Download Char: `40B50001-30B5-11E5-A151-FEFF819CDC90`

