using Microsoft.Maui.Devices;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;

//namespace app.Platforms.Windows
namespace app
{
    public class ZoomViewHandler : ContentViewHandler
    {
        protected override void ConnectHandler(ContentPanel platformView)
        {
            base.ConnectHandler(platformView);
            platformView.PointerWheelChanged += OnPointerWheelChanged;
        }

        protected override void DisconnectHandler(ContentPanel platformView)
        {
            platformView.PointerWheelChanged -= OnPointerWheelChanged;
            base.DisconnectHandler(platformView);
        }

        private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var pointer = e.GetCurrentPoint((UIElement)sender);
            var delta = pointer.Properties.MouseWheelDelta;
            var position = pointer.Position;

            var density = DeviceDisplay.MainDisplayInfo.Density;
            var mauiX = position.X / density;
            var mauiY = position.Y / density;

            if (VirtualView is ZoomView zoomView)
            {
                //zoomView.HandleWheel(delta, position.X, position.Y);
                zoomView.HandleWheel(delta, mauiX, mauiY);
            }
        }
    }
}
