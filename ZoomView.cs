using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace app
{
    public class ZoomView : ContentView
    {
        public ZoomView()
        {
            AnchorX = 0;
            AnchorY = 0;
        }

        // Called when the user tries to zoom out beyond the minimum (scale = 1)
        public Action? ZoomedOutBeyondMin { get; set; }

        public void HandleWheel(double delta, double x, double y)
        {
            ZoomAtPoint(delta, x, y);
        }

        void ZoomAtPoint(double delta, double x, double y)
        {
            var oldScale = Scale;
            var zoomFactor = (delta / 1200.0) + 1; // On windows delta = 120 or -120
            var desiredScale = oldScale * zoomFactor;

            if (desiredScale < 1.0)
            {
                Scale = 1.0;
                TranslationX = 0;
                TranslationY = 0;
                ZoomedOutBeyondMin?.Invoke();
                return;
            }

            var newScale = Math.Clamp(desiredScale, 1.0, 10);

            var tx = TranslationX;
            var ty = TranslationY;
            var scaleRatio = newScale / oldScale;

            var newTx = x - scaleRatio * (x - tx);
            var newTy = y - scaleRatio * (y - ty);

            Scale = newScale;
            TranslationX = newTx;
            TranslationY = newTy;
        }
    }

}
