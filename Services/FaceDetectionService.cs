using OpenCvSharp;
using System.Diagnostics;
using System.IO;

using Size = OpenCvSharp.Size;

namespace Gallery2.Services;

public class FaceDetectionService : IDisposable
{
    private const int MaxDim = 640;

    private readonly string modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "face_detection_yunet_2023mar_int8bq.onnx");
    private readonly float scoreThreshold = 0.8f;
    private readonly float nmsThreshold = 0.3f;

    private readonly Dictionary<Size, FaceDetectorYN> _detectors = new();

    public List<float[]> DetectFaces(Mat image)
    {
        // Downscale so the longer side is at most MaxDim; never upscale
        double scale = Math.Min(1.0, (double)MaxDim / Math.Max(image.Width, image.Height));
        var inputSize = new Size((int)(image.Width * scale), (int)(image.Height * scale));

        // Create a detector for each aspect ratio
        if (!_detectors.TryGetValue(inputSize, out var detector))
        {
            detector = FaceDetectorYN.Create(modelPath, "", inputSize,
                scoreThreshold: scoreThreshold, nmsThreshold: nmsThreshold);
            _detectors[inputSize] = detector;
        }

        using Mat input = image.Resize(inputSize);

        using Mat facesMat = new Mat();
        detector.Detect(input, facesMat);

        var results = new List<float[]>();

        if (facesMat is null || facesMat.Rows == 0)
            return results;

        double invScale = 1.0 / scale;

        // Scale results to original image coordinates
        for (int i = 0; i < facesMat.Rows; i++)
        {
            var row = new float[15];
            for (int j = 0; j < 15; j++)
                row[j] = facesMat.At<float>(i, j);

            if (scale < 1.0)
            {
                for (int k = 0; k < 14; k++)   // indices 0-3: bbox, 4-13: landmarks
                    row[k] = (float)(row[k] * invScale);
                // index 14 is the confidence score — no scaling
            }

            //var box = new Rect(
            //    (int)row[0], (int)row[1],
            //    (int)row[2], (int)row[3]);

            //box = new Rect(
            //    Math.Max(0, box.X),
            //    Math.Max(0, box.Y),
            //    Math.Min(box.Width, image.Width - box.X),
            //    Math.Min(box.Height, image.Height - box.Y));

            //if (box.Width <= 0 || box.Height <= 0) continue;

            results.Add(row);
        }

        return results;
    }

    public void Dispose()
    {
        foreach (var detector in _detectors.Values)
            detector.Dispose();
        _detectors.Clear();
    }
}