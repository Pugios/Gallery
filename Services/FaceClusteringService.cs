using Gallery2.Models;

namespace Gallery2.Services;

public class FaceClusteringService
{
    private readonly PersistenceService _persistenceService;

    // LFW-calibrated threshold is 0.363, but group-event photos score 0.65–0.85 between
    // different people due to varied poses/lighting. 0.75 sits in a natural gap in the
    // real distribution (same-person ~0.85+, different-person ~0.4–0.8).
    private const float threshold_cosine = 0.85f;


    // Working state: only used inside ClusterFaces, not persisted
    private record FaceCluster(float[] Centroid, int Count, string RepFilePath, Rect RepBoundingBox);

    public FaceClusteringService(PersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return dot / (float)(Math.Sqrt(magA) * Math.Sqrt(magB) + 1e-10);
    }

    public void ClusterFaces(IProgress<FaceIndexingProgress>? progress = null)
    {
        List<EmbeddingData> embeddings = _persistenceService.FaceEmbeddings;
        if (embeddings.Count == 0) return;

        _persistenceService.FaceClusters.Clear();
        _persistenceService.FaceIndex.Clear();

        var clusters = new Dictionary<string, FaceCluster>();

        // Calculate Cosine Similarity between each face
        for (int i = 0; i < embeddings.Count; i++)
        {
            EmbeddingData face = embeddings[i];
            string? bestId = null;
            float bestScore = float.MinValue;

            foreach (var (id, cluster) in clusters)
            {
                float score = CosineSimilarity(face.Embedding, cluster.Centroid);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = id;
                }
            }

            if (bestId != null && bestScore >= threshold_cosine)
            {
                var s = clusters[bestId];
                int newCount = s.Count + 1;
                var newCentroid = new float[face.Embedding.Length];
                for (int j = 0; j < newCentroid.Length; j++)
                    newCentroid[j] = (s.Centroid[j] * s.Count + face.Embedding[j]) / newCount;
                clusters[bestId] = s with { Centroid = newCentroid, Count = newCount };
            }
            else
            {
                clusters[Guid.NewGuid().ToString()] = new FaceCluster(
                    (float[])face.Embedding.Clone(), 1, face.FilePath, face.BoundingBox);
            }

            progress?.Report(new FaceIndexingProgress(i + 1, embeddings.Count * 2, "Clustering Faces (Pass 1)"));
        }

        // --- Pass 2: re-assign all embeddings using stable pass-1 centroids ---
        var clusterIds = clusters.Keys.ToList();
        var pass1Centroids = clusterIds.Select(id => clusters[id].Centroid).ToList();

        // Accumulate new centroids and track members
        int embLen = embeddings[0].Embedding.Length;
        var newCentroidSums = clusterIds.Select(_ => new float[embLen]).ToList();
        var newCounts = new int[clusterIds.Count];
        var memberLists = clusterIds.Select(_ => new List<int>()).ToList();

        for (int i = 0; i < embeddings.Count; i++)
        {
            var data = embeddings[i];
            int bestIdx = -1;
            float bestScore = float.MinValue;

            for (int c = 0; c < clusterIds.Count; c++)
            {
                float score = CosineSimilarity(data.Embedding, pass1Centroids[c]);
                if (score > bestScore) { bestScore = score; bestIdx = c; }
            }

            if (bestIdx >= 0 && bestScore >= threshold_cosine)
            {
                newCounts[bestIdx]++;
                for (int j = 0; j < embLen; j++)
                    newCentroidSums[bestIdx][j] += data.Embedding[j];
                memberLists[bestIdx].Add(i);
            }

            progress?.Report(new FaceIndexingProgress(embeddings.Count + i + 1, embeddings.Count * 2, "Clustering Faces (Pass 2)"));
        }

        // Finalize centroids
        var finalCentroids = new float[clusterIds.Count][];
        for (int c = 0; c < clusterIds.Count; c++)
        {
            finalCentroids[c] = new float[embLen];
            if (newCounts[c] == 0) continue;
            for (int j = 0; j < embLen; j++)
                finalCentroids[c][j] = newCentroidSums[c][j] / newCounts[c];
        }

        // --- Build FaceIndex and FaceClusters ---
        for (int c = 0; c < clusterIds.Count; c++)
        {
            if (memberLists[c].Count == 0) continue; // cluster absorbed nothing in pass 2

            // Representative: face whose embedding is closest to the final centroid
            int repIdx = memberLists[c]
                .MaxBy(i => CosineSimilarity(embeddings[i].Embedding, finalCentroids[c]));
            var rep = embeddings[repIdx];

            _persistenceService.FaceClusters[clusterIds[c]] = new ClusterData(
                finalCentroids[c],
                rep.FilePath,
                rep.BoundingBox,
                null);

            foreach (int memberIdx in memberLists[c])
            {
                var memberFile = embeddings[memberIdx].FilePath;
                _persistenceService.FaceIndex.AddOrUpdate(
                    memberFile,
                    [clusterIds[c]],
                    (_, existing) => existing.Contains(clusterIds[c])
                        ? existing
                        : [.. existing, clusterIds[c]]);
            }
        }
    }
}
