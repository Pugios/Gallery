using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.IO;
using System.Text;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace Gallery2.Services;

public class FaceEmbeddingService : IDisposable
{
    private readonly Net _net;

    public FaceEmbeddingService()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "face_recognition_sface_2021dec_int8bq.onnx");
        
        _net = Net.ReadNetFromONNX(modelPath);
    }

    public float[]? GetEmbedding(Mat image, Rect boundingBox)
    {
        using var crop = new Mat(image, boundingBox);

        using var blob = CvDnn.BlobFromImage(
            crop,
            scaleFactor: 1.0 / 127.5,
            size: new Size(112, 112),
            mean: new Scalar(127.5, 127.5, 127.5),
            swapRB: true);

        _net.SetInput(blob);

        using var output = _net.Forward();
        using var flat = output.Reshape(1, 1);

        flat.GetArray(out float[] embedding);
        return embedding.Length > 0 ? embedding : null;
    }

    public void Dispose() => _net.Dispose();
}