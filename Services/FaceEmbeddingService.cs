using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using Size = OpenCvSharp.Size;

namespace Gallery2.Services;

public class FaceEmbeddingService : IDisposable
{
    private const int FaceSize = 112;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly Lock _inferenceLock = new();

    public FaceEmbeddingService()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "webface_r50.onnx");

        using var options = new SessionOptions();

        // Try DirectML (GPU) first — works on any DirectX 12-capable GPU (NVIDIA, AMD, Intel).
        // ONNX Runtime automatically falls back to CPU for any op DirectML can't handle.
        try
        {
            options.AppendExecutionProvider_DML(deviceId: 0);
            Debug.WriteLine("[FaceEmbedding] DirectML execution provider registered.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FaceEmbedding] DirectML unavailable, using CPU. ({ex.Message})");
        }

        _session    = new InferenceSession(modelPath, options);
        _inputName  = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();

        Debug.WriteLine($"[FaceEmbedding] Session ready. Input='{_inputName}' Output='{_outputName}'");
    }

    public float[]? GetEmbedding(Mat image, float[] row)
    {
        var srcLandmarks = new Point2f[]
        {
            new(row[4],  row[5]),   // right eye
            new(row[6],  row[7]),   // left eye
            new(row[8],  row[9]),   // nose tip
            new(row[10], row[11]),  // right mouth corner
            new(row[12], row[13]),  // left mouth corner
        };

        using var transform = GetSimilarityTransformMatrix(srcLandmarks);
        using var aligned   = new Mat();
        Cv2.WarpAffine(image, aligned, transform, new Size(FaceSize, FaceSize));

        // Convert aligned BGR Mat → NCHW float32 tensor [1, 3, 112, 112]
        // Mirrors CvDnn.BlobFromImage(scaleFactor:1/127.5, mean:(127.5,127.5,127.5), swapRB:true)
        var inputData = new float[3 * FaceSize * FaceSize];
        for (int y = 0; y < FaceSize; y++)
        {
            for (int x = 0; x < FaceSize; x++)
            {
                var px = aligned.At<Vec3b>(y, x);   // OpenCV stores as BGR
                int offset = y * FaceSize + x;
                inputData[0 * FaceSize * FaceSize + offset] = (px.Item2 - 127.5f) / 127.5f; // R
                inputData[1 * FaceSize * FaceSize + offset] = (px.Item1 - 127.5f) / 127.5f; // G
                inputData[2 * FaceSize * FaceSize + offset] = (px.Item0 - 127.5f) / 127.5f; // B
            }
        }

        var tensor = new DenseTensor<float>(inputData, [1, 3, FaceSize, FaceSize]);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_inputName, tensor)
        };

        float[] embedding;
        lock (_inferenceLock)
        {
            using var results = _session.Run(inputs);
            embedding = results.First(r => r.Name == _outputName)
                               .AsEnumerable<float>()
                               .ToArray();
        }

        if (embedding.Length == 0) return null;

        // L2-normalise — mirrors OpenCV FaceRecognizerSF.feature()
        float mag = 0f;
        for (int i = 0; i < embedding.Length; i++) mag += embedding[i] * embedding[i];
        mag = (float)Math.Sqrt(mag);
        if (mag > 0)
            for (int i = 0; i < embedding.Length; i++) embedding[i] /= mag;

        return embedding;
    }

    // Direct C# port of OpenCV's getSimilarityTransformMatrix (face_recognize.cpp)
    // https://github.com/opencv/opencv/blob/4.x/modules/objdetect/src/face_recognize.cpp
    // Implements the Umeyama algorithm: optimal similarity transform (rotation + uniform scale + translation)
    private static Mat GetSimilarityTransformMatrix(Point2f[] src)
    {
        float[,] dst =
        {
            { 38.2946f, 51.6963f }, // right eye
            { 73.5318f, 51.5014f }, // left eye
            { 56.0252f, 71.7366f }, // nose tip
            { 41.5493f, 92.3655f }, // right mouth corner
            { 70.7299f, 92.2041f }, // left mouth corner
        };

        float srcMean0 = (src[0].X + src[1].X + src[2].X + src[3].X + src[4].X) / 5f;
        float srcMean1 = (src[0].Y + src[1].Y + src[2].Y + src[3].Y + src[4].Y) / 5f;

        const float dstMean0 = 56.0262f;
        const float dstMean1 = 71.9008f;

        var srcDemean = new float[5, 2];
        var dstDemean = new float[5, 2];
        for (int j = 0; j < 5; j++)
        {
            srcDemean[j, 0] = src[j].X - srcMean0;
            srcDemean[j, 1] = src[j].Y - srcMean1;
            dstDemean[j, 0] = dst[j, 0] - dstMean0;
            dstDemean[j, 1] = dst[j, 1] - dstMean1;
        }

        double a00 = 0, a01 = 0, a10 = 0, a11 = 0;
        for (int i = 0; i < 5; i++)
        {
            a00 += dstDemean[i, 0] * srcDemean[i, 0];
            a01 += dstDemean[i, 0] * srcDemean[i, 1];
            a10 += dstDemean[i, 1] * srcDemean[i, 0];
            a11 += dstDemean[i, 1] * srcDemean[i, 1];
        }
        a00 /= 5; a01 /= 5; a10 /= 5; a11 /= 5;

        using var A = new Mat(2, 2, MatType.CV_64F);
        A.At<double>(0, 0) = a00; A.At<double>(0, 1) = a01;
        A.At<double>(1, 0) = a10; A.At<double>(1, 1) = a11;

        double[] d = { 1.0, (a00 * a11 - a01 * a10) < 0 ? -1.0 : 1.0 };

        using var sVals = new Mat();
        using var u     = new Mat();
        using var vt    = new Mat();
        SVD.Compute(A, sVals, u, vt);

        double s0  = sVals.At<double>(0);
        double s1  = sVals.At<double>(1);
        double tol = Math.Max(s0, s1) * 2.0 * 1.17549435e-38;
        int rank   = (s0 > tol ? 1 : 0) + (s1 > tol ? 1 : 0);

        double detU  = u.At<double>(0, 0)  * u.At<double>(1, 1)  - u.At<double>(0, 1)  * u.At<double>(1, 0);
        double detVt = vt.At<double>(0, 0) * vt.At<double>(1, 1) - vt.At<double>(0, 1) * vt.At<double>(1, 0);

        double t00, t01, t10, t11;
        if (rank == 1)
        {
            if (detU * detVt > 0)
            {
                using var uvt = (Mat)(u * vt);
                t00 = uvt.At<double>(0, 0); t01 = uvt.At<double>(0, 1);
                t10 = uvt.At<double>(1, 0); t11 = uvt.At<double>(1, 1);
            }
            else
            {
                using var D    = new Mat(2, 2, MatType.CV_64F, Scalar.All(0));
                D.At<double>(0, 0) = d[0]; D.At<double>(1, 1) = -1.0;
                using var Dvt  = (Mat)(D * vt);
                using var uDvt = (Mat)(u * Dvt);
                t00 = uDvt.At<double>(0, 0); t01 = uDvt.At<double>(0, 1);
                t10 = uDvt.At<double>(1, 0); t11 = uDvt.At<double>(1, 1);
            }
        }
        else
        {
            using var D    = new Mat(2, 2, MatType.CV_64F, Scalar.All(0));
            D.At<double>(0, 0) = d[0]; D.At<double>(1, 1) = d[1];
            using var Dvt  = (Mat)(D * vt);
            using var uDvt = (Mat)(u * Dvt);
            t00 = uDvt.At<double>(0, 0); t01 = uDvt.At<double>(0, 1);
            t10 = uDvt.At<double>(1, 0); t11 = uDvt.At<double>(1, 1);
        }

        double var1 = 0, var2 = 0;
        for (int i = 0; i < 5; i++)
        {
            var1 += srcDemean[i, 0] * srcDemean[i, 0];
            var2 += srcDemean[i, 1] * srcDemean[i, 1];
        }
        var1 /= 5; var2 /= 5;

        double scale = 1.0 / (var1 + var2) * (s0 * d[0] + s1 * d[1]);

        double t02 = dstMean0 - scale * (t00 * srcMean0 + t01 * srcMean1);
        double t12 = dstMean1 - scale * (t10 * srcMean0 + t11 * srcMean1);

        var result = new Mat(2, 3, MatType.CV_64F);
        result.At<double>(0, 0) = scale * t00; result.At<double>(0, 1) = scale * t01; result.At<double>(0, 2) = t02;
        result.At<double>(1, 0) = scale * t10; result.At<double>(1, 1) = scale * t11; result.At<double>(1, 2) = t12;
        return result;
    }

    public void Dispose() => _session.Dispose();
}
