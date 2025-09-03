using BLE_Interface.Helpers; // ← Required for OrientationVisualizer
using BLE_Interface.Services;
using BLE_Interface.ViewModels;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace BLE_Interface.Views
{
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; }

        private OrientationVisualizer _orientationVisualizer;

        private readonly IBLEClient _ble; // your existing concrete BLE client

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
    }
}
