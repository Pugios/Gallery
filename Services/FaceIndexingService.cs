using Gallery2.Models;
using OpenCvSharp;
using System.Diagnostics;
using Rect = System.Windows.Rect;
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

    public async Task DeleteIndex(IProgress<FaceIndexingProgress>? progress = null)
    {
        progress?.Report(new FaceIndexingProgress(0, 1, "Deleting Index"));
        await Task.Run(() =>
        {
            _persistenceService.FaceEmbeddings.Clear();
            _persistenceService.FaceClusters.Clear();
            _persistenceService.FaceIndex.Clear();
            _persistenceService.SaveFaceEmbeddings();
            _persistenceService.SaveFaceClusters();
            _persistenceService.SaveFaceIndex();
        });
    }

    public async Task DissolveClusterAsync(string clusterId)
    {
        await Task.Run(() => _clusterer.DissolveCluster(clusterId));
        _persistenceService.SaveFaceClusters();
        _persistenceService.SaveFaceIndex();
    }

    public async Task IndexUnprocessedAsync(IProgress<FaceIndexingProgress>? progress = null)
    {
        var faceIndex = _persistenceService.FaceIndex;

        var unprocessed = _persistenceService.MetadataCache
            .Where(kvp => !faceIndex.ContainsKey(kvp.Key))
            .Select(kvp => new PictureItem(kvp.Value))
            .ToList();

        await IndexImagesAsync(unprocessed, progress);
    }

    private async Task IndexImagesAsync(List<PictureItem> pictures, IProgress<FaceIndexingProgress>? progress = null)
    {
        if (pictures.Count == 0) return;

        var alreadyEmbedded = _persistenceService.FaceEmbeddings
            .Select(e => e.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toEmbed = pictures.Where(p => !alreadyEmbedded.Contains(p.FilePath)).ToList();

        // Detect and Embed Faces
        progress?.Report(new FaceIndexingProgress(0, toEmbed.Count, "Detecting and Embedding Faces"));
        await Task.Run(() =>
        {
            for (int i = 0; i < toEmbed.Count; i++)
            {
                DetectAndEmbed(toEmbed[i]);
                progress?.Report(new FaceIndexingProgress(i + 1, toEmbed.Count, "Detecting and Embedding Faces"));
            }
        });

        _persistenceService.SaveFaceEmbeddings();

        // Cluster Faces
        progress?.Report(new FaceIndexingProgress(0, pictures.Count, "Clustering Faces"));
        await Task.Run(() =>
        {
            _clusterer.ClusterFaces(progress);
        });

        _persistenceService.SaveFaceClusters();
        _persistenceService.SaveFaceIndex();
    }

    private void DetectAndEmbed(PictureItem picture)
    {
        if (_persistenceService.FaceEmbeddings.Any(e =>
              string.Equals(e.FilePath, picture.FilePath, StringComparison.OrdinalIgnoreCase)))
            return;

        using var image = Cv2.ImRead(picture.FilePath);
        if (image.Empty()) return;

        var detectedFaces = _detector.DetectFaces(image);

        if (detectedFaces.Count == 0)
        {
            // Mark as processed so incremental re-indexes don't re-detect this image every time.
            _persistenceService.FaceIndex.TryAdd(picture.FilePath, []);
            return;
        }

        foreach (var face in detectedFaces)
        {
            var embedding = _embedder.GetEmbedding(image, face);
            if (embedding is null) continue;

            var faceRect = new Rect(
                (int)face[0], (int)face[1],
                (int)face[2], (int)face[3]);

            float confidence = face[14];
            _persistenceService.FaceEmbeddings.Add(new EmbeddingData(picture.FilePath, faceRect, embedding, confidence));
        }
    }
}
