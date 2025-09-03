using System;
using System.ComponentModel;            // ICollectionView
using System.Windows;
using System.Windows.Data;              // CollectionViewSource
using System.Windows.Threading;
using BLE_Interface.Services.Logging;

namespace BLE_Interface.Views
{
    public partial class LogWindow : Window
    {
        private ICollectionView _view;

        public LogWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Hook the ItemsSource in code (avoids the XAML static reference issue)
            LogList.ItemsSource = Log.Items;

            // Get a filterable view over the collection
            _view = CollectionViewSource.GetDefaultView(Log.Items);
            if (_view != null)
            {
                _view.Filter = ShouldShow;
                _view.Refresh();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_view != null) _view.Filter = null;
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => { if (_view != null) _view.Refresh(); }),
                                   DispatcherPriority.Background);
        }

        private bool ShouldShow(object item)
        {
            var line = item as string;
            if (line == null) return false;

            var text = line.ToLowerInvariant();

            bool hasBattery = text.Contains("battery");
            bool hasImu = text.Contains("imu");
            bool hasBreath = text.Contains("breath");
            bool hasStretch = text.Contains("stretch");
            bool hasPress = text.Contains("pressure");
            bool hasTemp = text.Contains("temp");
            bool hasHR = text.Contains("hr") || text.Contains("heart rate");

            bool isGeneral = !(hasBattery || hasImu || hasBreath || hasStretch || hasPress || hasTemp || hasHR);

            bool showBattery = BatteryCheck.IsChecked == true;
            bool showImu = IMUCheck.IsChecked == true;
            bool showBreath = BreathingCheck.IsChecked == true;
            bool showStretch = StretchCheck.IsChecked == true;
            bool showPressTmp = PressureTempCheck.IsChecked == true;
            bool showHR = HRCheck.IsChecked == true;
            bool showGeneral = GeneralCheck.IsChecked == true;

            if (hasBattery && showBattery) return true;
            if (hasImu && showImu) return true;
            if (hasBreath && showBreath) return true;
            if (hasStretch && showStretch) return true;
            if ((hasPress || hasTemp) && showPressTmp) return true;
            if (hasHR && showHR) return true;
            if (isGeneral && showGeneral) return true;

            return false;
        }
    }
}
