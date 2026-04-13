using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.Face;
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
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Models",
            "face_recognition_sface_2021dec.onnx");
        _net = Net.ReadNetFromONNX(modelPath);
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
        using var aligned = new Mat();
        Cv2.WarpAffine(image, aligned, transform, new Size(112, 112));

        using var blob = CvDnn.BlobFromImage(
            aligned,
            scaleFactor: 1.0 / 127.5,
            size: new Size(112, 112),
            mean: new Scalar(127.5, 127.5, 127.5),
            swapRB: true);

        _net.SetInput(blob);

        // "fc1" is the layer name OpenCV's own FaceRecognizerSF::feature() uses.
        // Forward() with no arg returns the last topological layer, which may differ.
        using var output = _net.Forward("fc1");
        using var flat = output.Reshape(1, 1);

        flat.GetArray(out float[] embedding);
        if (embedding.Length == 0) return null;

        // L2-normalize — mirrors OpenCV FaceRecognizerSF.feature() which normalizes internally
        float magnitude = 0f;
        for (int i = 0; i < embedding.Length; i++)
            magnitude += embedding[i] * embedding[i];
        magnitude = (float)Math.Sqrt(magnitude);
        if (magnitude > 0)
            for (int i = 0; i < embedding.Length; i++)
                embedding[i] /= magnitude;

        return embedding;
    }

    // Direct C# port of OpenCV's getSimilarityTransformMatrix (face_recognize.cpp)
    // https://github.com/opencv/opencv/blob/4.x/modules/objdetect/src/face_recognize.cpp
    // Implements the Umeyama algorithm: optimal similarity transform (rotation + uniform scale + translation)
    private static Mat GetSimilarityTransformMatrix(Point2f[] src)
    {
        // Reference landmark positions in the 112×112 aligned output — from OpenCV source
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

        // dst_mean is hardcoded in the C++ source, not computed from dst[]
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

        // 2×2 cross-covariance matrix
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

        // d[1] = -1 when det(A) < 0 to handle reflections
        double[] d = { 1.0, (a00 * a11 - a01 * a10) < 0 ? -1.0 : 1.0 };

        using var sVals = new Mat();
        using var u = new Mat();
        using var vt = new Mat();
        SVD.Compute(A, sVals, u, vt);

        double s0 = sVals.At<double>(0);
        double s1 = sVals.At<double>(1);

        // Rank check — guards against degenerate input; always 2 for real face data
        double tol = Math.Max(s0, s1) * 2.0 * 1.17549435e-38; // 2 * FLT_MIN
        int rank = (s0 > tol ? 1 : 0) + (s1 > tol ? 1 : 0);

        double detU = u.At<double>(0, 0) * u.At<double>(1, 1) - u.At<double>(0, 1) * u.At<double>(1, 0);
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
                // d[1] temporarily forced to -1 as in C++ source
                using var D = new Mat(rows: 2, cols: 2, type: MatType.CV_64F, Scalar.All(0));
                D.At<double>(0, 0) = d[0];
                D.At<double>(1, 1) = -1.0;
                using var Dvt = (Mat)(D * vt);
                using var uDvt = (Mat)(u * Dvt);
                t00 = uDvt.At<double>(0, 0); t01 = uDvt.At<double>(0, 1);
                t10 = uDvt.At<double>(1, 0); t11 = uDvt.At<double>(1, 1);
            }
        }
        else
        {
            using var D = new Mat(rows: 2, cols: 2, type: MatType.CV_64F, Scalar.All(0));
            D.At<double>(0, 0) = d[0];
            D.At<double>(1, 1) = d[1];
            using var Dvt = (Mat)(D * vt);
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
        result.At<double>(0, 0) = scale * t00; result.At<double>(0, 1) = scale * t01; result.At<double>(0, 2) =
t02;
        result.At<double>(1, 0) = scale * t10; result.At<double>(1, 1) = scale * t11; result.At<double>(1, 2) =
t12;
        return result;
    }

    public void Dispose() => _net.Dispose();
}
