using System.ComponentModel;
using System.Windows.Media;

namespace BLE_Interface.Models
{
    public enum DeviceConnectionState
    {
        Disconnected,  // Black (default)
        Connecting,    // Orange
        Connected      // Green
    }

    public class BleDeviceModel : INotifyPropertyChanged
    {
        private string _address;
        private string _name;
        private int _rssi;
        private int _batteryPercent;
        private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;

        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(nameof(Address)); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public int Rssi
        {
            get => _rssi;
            set { _rssi = value; OnPropertyChanged(nameof(Rssi)); }
        }

        public int BatteryPercent
        {
            get => _batteryPercent;
            set { _batteryPercent = value; OnPropertyChanged(nameof(BatteryPercent)); }
        }

        public DeviceConnectionState ConnectionState
        {
            get => _connectionState;
            set
            {
                if (_connectionState != value)
                {
                    _connectionState = value;
                    OnPropertyChanged(nameof(ConnectionState));
                    OnPropertyChanged(nameof(NameColor));
                }
            }
        }

        public Brush NameColor
        {
            get
            {
                return ConnectionState switch
                {
                    DeviceConnectionState.Connecting => new SolidColorBrush(Color.FromRgb(255, 152, 0)), // Orange
                    DeviceConnectionState.Connected => new SolidColorBrush(Color.FromRgb(76, 175, 80)),  // Green
                    _ => new SolidColorBrush(Color.FromRgb(51, 51, 51))  // Dark gray (default)
                };
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}