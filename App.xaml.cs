using System.Windows;
using BLE_Interface.Views;

namespace BLE_Interface
{
    public partial class App : Application
    {
        private LogWindow _logWindow;
        private ScanTestWindow _scanWindow;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Main window is shown via App.xaml StartupUri="Views/MainWindow.xaml"

            _logWindow = new LogWindow();
            _logWindow.Show();

            _scanWindow = new ScanTestWindow();
            _scanWindow.Show();
        }
    }
}
