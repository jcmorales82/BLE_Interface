using BLE_Interface.ViewModels;
using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace BLE_Interface.Views
{
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; }
        private bool _isDrawerOpen = false;

        public MainWindow()
        {
            InitializeComponent();

            // Create and set ViewModel
            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;

            // Handle window closing
            Closing += OnWindowClosing;
            
            // Setup auto-scroll for logs
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.LogMessages))
                {
                    // Auto-scroll to bottom when new log message is added
                    LogScrollViewer.ScrollToBottom();
                }
            };
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Clean up resources
            try
            {
                ViewModel?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleDrawer();
        }

        private void CloseDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDrawer();
        }

        private void DrawerOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseDrawer();
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the drawer after clicking Scan
            CloseDrawer();
        }

        private async void DeviceList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lbDevices.SelectedItem is BLE_Interface.Models.BleDeviceModel device)
            {
                await ViewModel.ConnectToDeviceAsync(device);
            }
        }

        private void ToggleDrawer()
        {
            if (_isDrawerOpen)
                CloseDrawer();
            else
                OpenDrawer();
        }

        private void OpenDrawer()
        {
            _isDrawerOpen = true;
            DrawerOverlay.Visibility = Visibility.Visible;
            DrawerPanel.Visibility = Visibility.Visible;

            // Animate overlay fade in
            var fadeIn = new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(300));
            DrawerOverlay.BeginAnimation(OpacityProperty, fadeIn);

            // Animate drawer slide in
            var slideIn = new DoubleAnimation(-280, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            DrawerTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
        }

        private void CloseDrawer()
        {
            _isDrawerOpen = false;

            // Animate overlay fade out
            var fadeOut = new DoubleAnimation(0.5, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) => DrawerOverlay.Visibility = Visibility.Collapsed;
            DrawerOverlay.BeginAnimation(OpacityProperty, fadeOut);

            // Animate drawer slide out
            var slideOut = new DoubleAnimation(0, -280, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            slideOut.Completed += (s, e) => DrawerPanel.Visibility = Visibility.Collapsed;
            DrawerTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
        }
    }
}