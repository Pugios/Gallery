using System;
using System.Collections.Generic;
using System.Text;

namespace app
{
    public class ZoomView : ContentView
    {
        public void HandleWheel(double delta, double x, double y)
        {
            ZoomAtPoint(delta, x, y);
        }

        void ZoomAtPoint(double delta, double cx, double cy)
        {
            var oldScale = Scale;
            var zoomFactor = 1 + (delta / 1200.0);
            var newScale = Math.Clamp(oldScale * zoomFactor, 0.25, 10);

            if (Math.Abs(newScale - oldScale) < 0.0001)
                return;

            var tx = TranslationX;
            var ty = TranslationY;

            var scaleRatio = newScale / oldScale;

            var newTx = cx - scaleRatio * (cx - tx);
            var newTy = cy - scaleRatio * (cy - ty);

            Scale = newScale;
            TranslationX = newTx;
            TranslationY = newTy;
        }
    }

}
