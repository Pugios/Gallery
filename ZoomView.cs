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

        public void HandleWheel(double delta, double x, double y)
        {
            ZoomAtPoint(delta, x, y);
        }

        void ZoomAtPoint(double delta, double x, double y)
        {
            var oldScale = Scale;
            var zoomFactor = (delta / 1200.0) + 1; // On windows delta = 120 or -120
            var newScale = Math.Clamp(oldScale * zoomFactor, 0.25, 10);

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
