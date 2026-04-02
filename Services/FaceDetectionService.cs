using OpenCvSharp;
using System.IO;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace Gallery2.Services;

public record FaceDetectionResult(
      Rect BoundingBox,
      float[] LandmarkRow
  );

public class FaceDetectionService : IDisposable
{
    private readonly FaceDetectorYN _detector;

    public FaceDetectionService()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "face_detection_yunet_2023mar_int8bq.onnx");

        _detector = FaceDetectorYN.Create(modelPath, "", new Size(500, 500),
            scoreThreshold: 0.9f, nmsThreshold: 0.3f);
    }

    public List<FaceDetectionResult> DetectFaces(Mat image)
    {
        using Mat facesMat = new Mat();
        _detector.Detect(image, facesMat);

        var results = new List<FaceDetectionResult>();

        if (facesMat is null || facesMat.Rows == 0)
            return results;

        for (int i = 0; i < facesMat.Rows; i++)
        {
            var row = new float[15];
            for (int j = 0; j < 15; j++)
                row[j] = facesMat.At<float>(i, j);

            var box = new Rect(
                (int)row[0], (int)row[1],
                (int)row[2], (int)row[3]);

            box = new Rect(
                Math.Max(0, box.X),
                Math.Max(0, box.Y),
                Math.Min(box.Width, image.Width - box.X),
                Math.Min(box.Height, image.Height - box.Y));

            if (box.Width <= 0 || box.Height <= 0) continue;

            results.Add(new FaceDetectionResult(box, row));
        }

        return results;
    }
    public void Dispose() => _detector.Dispose();
}