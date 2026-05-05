using Gallery2.Models;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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

    // Raised on the UI thread whenever indexing starts (true) or finishes (false).
    public event Action<bool>? IndexingStateChanged;
    // Raised on the UI thread for each progress update during indexing.
    public event Action<FaceIndexingProgress>? ProgressChanged;

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

    public async Task RemoveFolderFromIndexAsync(string folderPath)
    {
        await Task.Run(() =>
        {
            var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

            bool InFolder(string path) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            // Collect cluster IDs referenced by files in this folder before removing them
            var affectedClusterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _persistenceService.FaceIndex)
                if (InFolder(kvp.Key))
                    foreach (var id in kvp.Value)
                        affectedClusterIds.Add(id);

            // Remove from FaceIndex and MetadataCache
            foreach (var key in _persistenceService.FaceIndex.Keys.Where(InFolder).ToList())
                _persistenceService.FaceIndex.TryRemove(key, out _);

            foreach (var key in _persistenceService.MetadataCache.Keys.Where(InFolder).ToList())
                _persistenceService.MetadataCache.TryRemove(key, out _);

            // Rebuild FaceEmbeddings without the removed folder
            var kept = _persistenceService.FaceEmbeddings.Where(e => !InFolder(e.FilePath)).ToList();
            _persistenceService.FaceEmbeddings.Clear();
            foreach (var e in kept)
                _persistenceService.FaceEmbeddings.Add(e);

            // Repair each affected cluster
            foreach (var clusterId in affectedClusterIds)
            {
                if (!_persistenceService.FaceClusters.TryGetValue(clusterId, out var cluster))
                    continue;

                bool hasRemainingImages = _persistenceService.FaceIndex
                    .Any(kvp => kvp.Value.Contains(clusterId, StringComparer.OrdinalIgnoreCase));

                if (!hasRemainingImages)
                {
                    _persistenceService.FaceClusters.TryRemove(clusterId, out _);
                    continue;
                }

                // Update representative if it was inside the removed folder
                if (!InFolder(cluster.RepresentativeFilePath)) continue;

                var bestRep = _persistenceService.FaceEmbeddings
                    .Where(e => _persistenceService.FaceIndex.TryGetValue(e.FilePath, out var ids)
                                && ids.Contains(clusterId, StringComparer.OrdinalIgnoreCase))
                    .MaxBy(e => FaceClusteringService.CosineSimilarity(e.Embedding, cluster.Centroid));

                if (bestRep is null)
                    _persistenceService.FaceClusters.TryRemove(clusterId, out _);
                else
                    _persistenceService.FaceClusters[clusterId] = cluster with
                    {
                        RepresentativeFilePath = bestRep.FilePath,
                        RepresentativeBoundingBox = bestRep.BoundingBox
                    };
            }

            _persistenceService.SaveMetadataCache(_persistenceService.MetadataCache.Values);
            _persistenceService.SaveFaceEmbeddings();
            _persistenceService.SaveFaceClusters();
            _persistenceService.SaveFaceIndex();
        });
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Indexing Logic
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Find all unprocessed images (not in FaceIndex)
    public async Task IndexUnprocessedAsync(IProgress<FaceIndexingProgress>? externalProgress = null)
    {
        IndexingStateChanged?.Invoke(true);
        try
        {
            var faceIndex = _persistenceService.FaceIndex;
            var unprocessed = _persistenceService.MetadataCache
                .Where(kvp => !faceIndex.ContainsKey(kvp.Key))
                .Select(kvp => new PictureItem(kvp.Value))
                .ToList();

            // Wrap so every update goes to both the service-level event and any external reporter.
            var progress = new Progress<FaceIndexingProgress>(p =>
            {
                ProgressChanged?.Invoke(p);
                externalProgress?.Report(p);
            });

            await IndexImagesAsync(unprocessed, progress);
        }
        finally
        {
            IndexingStateChanged?.Invoke(false);
        }
    }

    // Working in Parallel, for each image detect faces, generate embeddings, and store results in FaceEmbeddings
    // Then cluster all embeddings and store clusters in FaceClusters & FaceIndex
    private async Task IndexImagesAsync(List<PictureItem> pictures, IProgress<FaceIndexingProgress>? progress = null)
    {
        if (pictures.Count == 0) return;

        var alreadyEmbedded = _persistenceService.FaceEmbeddings
            .Select(e => e.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toEmbed = pictures.Where(p => !alreadyEmbedded.Contains(p.FilePath)).ToList();

        // Detect and Embed Faces

        var collected = new ConcurrentBag<EmbeddingData>();
        int done = 0;
        progress?.Report(new FaceIndexingProgress(0, toEmbed.Count, "Detecting and Embedding Faces"));
        await Task.Run(() =>
        {
            Parallel.ForEach(toEmbed,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                picture =>
                {
                    foreach (var e in DetectAndEmbed(picture))
                        collected.Add(e);
                    int n = Interlocked.Increment(ref done);
                    progress?.Report(new FaceIndexingProgress(n, toEmbed.Count, "Detecting and Embedding Faces"));
                });
        });
        foreach (var e in collected)
            _persistenceService.FaceEmbeddings.Add(e);

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

    private List<EmbeddingData> DetectAndEmbed(PictureItem picture)
    {
        var results = new List<EmbeddingData>();
        using var image = Cv2.ImRead(picture.FilePath);
        if (image.Empty()) return results;

        var detectedFaces = _detector.DetectFaces(image);

        if (detectedFaces.Count == 0)
        {
            // Mark as processed so incremental re-indexes don't re-detect this image every time.
            _persistenceService.FaceIndex.TryAdd(picture.FilePath, []);
            return results;
        }

        foreach (var face in detectedFaces)
        {
            var embedding = _embedder.GetEmbedding(image, face);
            if (embedding is null) continue;
            results.Add(new EmbeddingData(picture.FilePath,
                new Rect((int)face[0], (int)face[1], (int)face[2], (int)face[3]),
                embedding, face[14]));
        }
        return results;
    }
}
