using System.ComponentModel;

namespace BLE_Interface.Models
{
    public class BleDeviceModel : INotifyPropertyChanged
    {
        private string _address;
        private string _name;
        private int _rssi;
        private int _batteryPercent;

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}