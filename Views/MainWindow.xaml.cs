using BLE_Interface.ViewModels;
using System;
using System.Windows;

namespace BLE_Interface.Views
{
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            // Create and set ViewModel
            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;

            // Handle window closing
            Closing += OnWindowClosing;
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
    }
}