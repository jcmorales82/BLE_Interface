using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Media3D;
using BLE_Interface.ViewModels;

namespace BLE_Interface.Helpers
{
    public class OrientationVisualizer
    {
        private readonly QuaternionRotation3D _quaternionRotation;
        private readonly RotateTransform3D _rotationTransform;

        public RotateTransform3D Transform => _rotationTransform;

        public OrientationVisualizer(MainWindowViewModel viewModel)
        {
            _quaternionRotation = new QuaternionRotation3D();
            _rotationTransform = new RotateTransform3D(_quaternionRotation);

            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Orientation))
                {
                    var q = viewModel.Orientation;

                    // Make sure we update from the UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _quaternionRotation.Quaternion = q;
                    });
                }
            };
        }
    }
}