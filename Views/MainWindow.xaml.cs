using System.Windows;
using System.Windows.Controls;
using BLE_Interface.ViewModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using BLE_Interface.Helpers; // ← Required for OrientationVisualizer
using System.Windows.Media.Media3D;

namespace BLE_Interface.Views
{
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; }

        private OrientationVisualizer _orientationVisualizer;

        public MainWindow()
        {
            InitializeComponent();

            // Expose ViewModel
            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;

            // Hook up the OrientationVisualizer
            _orientationVisualizer = new OrientationVisualizer(ViewModel);

            // Apply to the 3D cube in XAML (named "CubeModel")
            if (CubeModel != null)
            {
                CubeModel.Transform = _orientationVisualizer.Transform;
            }
        }

        private void tbLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            tbLog.ScrollToEnd();
        }
    }
}
