using OpenCvSharp;
using System.IO;
using Size = OpenCvSharp.Size;

namespace Gallery2.Services;

public class FaceDetectionService : IDisposable
{
    private const int MaxDim = 640;

    private readonly string _modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "face_detection_yunet_2023mar_int8bq.onnx");
    private readonly float _scoreThreshold = 0.8f;
    private readonly float _nmsThreshold   = 0.3f;

    // One detector dictionary per thread — FaceDetectorYN is not thread-safe.
    // trackAllValues: true lets Dispose() enumerate and clean up every thread's instances.
    private readonly ThreadLocal<Dictionary<Size, FaceDetectorYN>> _threadDetectors =
        new(() => new Dictionary<Size, FaceDetectorYN>(), trackAllValues: true);

    public List<float[]> DetectFaces(Mat image)
    {
        // Downscale so the longer side is at most MaxDim; never upscale
        double scale     = Math.Min(1.0, (double)MaxDim / Math.Max(image.Width, image.Height));
        var    inputSize = new Size((int)(image.Width * scale), (int)(image.Height * scale));

        // Each thread has its own detector pool keyed by input size
        var detectors = _threadDetectors.Value!;
        if (!detectors.TryGetValue(inputSize, out var detector))
        {
            detector = FaceDetectorYN.Create(_modelPath, "", inputSize,
                scoreThreshold: _scoreThreshold, nmsThreshold: _nmsThreshold);
            detectors[inputSize] = detector;
        }

        using Mat input    = image.Resize(inputSize);
        using Mat facesMat = new Mat();
        detector.Detect(input, facesMat);

        var results = new List<float[]>();
        if (facesMat is null || facesMat.Rows == 0) return results;

        double invScale = 1.0 / scale;
        for (int i = 0; i < facesMat.Rows; i++)
        {
            var row = new float[15];
            for (int j = 0; j < 15; j++)
                row[j] = facesMat.At<float>(i, j);

            if (scale < 1.0)
                for (int k = 0; k < 14; k++)   // bbox + landmarks; skip confidence at [14]
                    row[k] = (float)(row[k] * invScale);

            results.Add(row);
        }

        return results;
    }

    public void Dispose()
    {
        // ThreadLocal.Values enumerates every per-thread dictionary that was created
        foreach (var dict in _threadDetectors.Values)
        {
            foreach (var detector in dict.Values)
                detector.Dispose();
        }
        _threadDetectors.Dispose();
    }
}
