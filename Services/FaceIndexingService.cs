using Gallery2.Models;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace Gallery2.Services;
public class FaceIndexingService
{
    private readonly FaceDetectionService _detector;
    private readonly FaceEmbeddingService _embedder;
    private readonly FaceClusteringService _clusterer;
    private readonly PersistenceService _persistenceService;

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".webp"];

    public FaceIndexingService(FaceDetectionService detector, FaceEmbeddingService embedder, FaceClusteringService clusterer, PersistenceService persistenceService)
    {
        _detector = detector;
        _embedder = embedder;
        _clusterer = clusterer;
        _persistenceService = persistenceService;
    }

    public async Task IndexUnprocessedAsync(IProgress<FaceIndexingProgress>? progress = null)
    {
        var faceIndex = _persistenceService.FaceIndex;
        // metadataCache Keys contain all image paths
        var metadataCache = _persistenceService.MetadataCache;

        // Find files that are not yet indexed
        var unprocessed = metadataCache.Keys
            .Where(path => !faceIndex.ContainsKey(path))
            .ToList();

        await IndexFilesAsync(unprocessed, progress);
    }

    private async Task IndexFilesAsync(List<string> files, IProgress<FaceIndexingProgress>? progress = null)
    {
        if (files.Count == 0) return;

        var alreadyEmbedded = _persistenceService.FaceEmbeddings
            .Select(e => e.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filesToEmbed = files.Where(e => !alreadyEmbedded.Contains(e)).ToList();

        // Detect and Embed Faces
        progress?.Report(new FaceIndexingProgress(0, filesToEmbed.Count, 1, "Detecting Faces"));
        await Task.Run(() =>
        {
            for (int i = 0; i < filesToEmbed.Count; i++)
            {
                ProcessImage(filesToEmbed[i]);
                progress?.Report(new FaceIndexingProgress(i + 1, filesToEmbed.Count, 1, "Detecting Faces"));
            }
        });

        _persistenceService.SaveFaceEmbeddings();

        // Cluster Faces
        progress?.Report(new FaceIndexingProgress(0, files.Count, 2, "Clustering Faces"));
        await Task.Run(() =>
        {
            _clusterer.ClusterFaces(_persistenceService.FaceEmbeddings, progress);
        });

        _persistenceService.SaveFaceClusters();
        _persistenceService.SaveFaceIndex();
    }

    private void ProcessImage(string imagePath)
    {
        if (_persistenceService.FaceEmbeddings.Any(e =>
              string.Equals(e.FilePath, imagePath, StringComparison.OrdinalIgnoreCase)))
            return;

        // Read Image
        using var original = Cv2.ImRead(imagePath);
        if (original.Empty()) return;

        using var resized = new Mat();
        Cv2.Resize(original, resized, new Size(500, 500));

        // Detect Faces
        var detectedFaces = _detector.DetectFaces(resized);

        foreach (var face in detectedFaces)
        {
            // Embed Faces
            var embedding = _embedder.GetEmbedding(resized, face.BoundingBox);
            if (embedding is null) continue;

            var faceRect = new FaceRect(
              face.BoundingBox.X,
              face.BoundingBox.Y,
              face.BoundingBox.Width,
              face.BoundingBox.Height);

            // Store Embeddings
            _persistenceService.FaceEmbeddings.Add(new EmbeddingData(imagePath, faceRect, embedding));
        }
    }
}
