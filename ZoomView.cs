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
            var zoomFactor = 1 + (delta / 1200.0);
            var newScale = Math.Clamp(oldScale * zoomFactor, 0.25, 10);

            if (Math.Abs(newScale - oldScale) < 0.0001)
                return;

            var tx = TranslationX;
            var ty = TranslationY;
            var scaleRatio = newScale / oldScale;

            var newTx = x - scaleRatio * (x - tx);
            var newTy = y - scaleRatio * (y - ty);

            Debug.WriteLine($"ZoomView | " +
                $"Scale: {oldScale} -> {newScale}, \n" +
                $"Tx: {tx} -> {newTx}, \n" +
                $"Ty: {ty} -> {newTy}");

            Scale = newScale;
            TranslationX = newTx;
            TranslationY = newTy;
        }
    }

}
