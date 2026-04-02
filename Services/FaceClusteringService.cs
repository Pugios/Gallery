using Gallery2.Models;
namespace Gallery2.Services;

public class FaceClusteringService
{
    private readonly PersistenceService _persistenceService;

    public FaceClusteringService(PersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must be of same length");
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return dot / (float)(Math.Sqrt(magA) * Math.Sqrt(magB) + 1e-10); // Add small value to prevent division by zero
    }

    public void ClusterFaces(List<EmbeddingData> embeddings, IProgress<FaceIndexingProgress>? progress = null)
    {
        if (embeddings.Count == 0) return;

        _persistenceService.FaceClusters.Clear();
        _persistenceService.FaceIndex.Clear();

        const float Threshold = 0.28f;
        var clusters = new Dictionary<string, ClusterState>();

        for (int i = 0; i < embeddings.Count; i++)
        {
            EmbeddingData data = embeddings[i];
            string? bestId = null;
            float bestScore = float.MinValue;

            foreach (var (id, state) in clusters)
            {
                float score = CosineSimilarity(data.Embedding, state.Centroid);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = id;
                }
            }

            string assignedId;
            if (bestId != null && bestScore >= Threshold)
            {
                var s = clusters[bestId];
                var newCount = s.Count + 1;
                var newCentroid = new float[s.Centroid.Length];
                for (int j = 0; j < newCentroid.Length; j++)
                    newCentroid[j] = (s.Centroid[j] * s.Count + data.Embedding[j]) / newCount;

                clusters[bestId] = s with { Centroid = newCentroid, Count = newCount };
                assignedId = bestId;
            }
            else
            {
                // New cluster
                assignedId = Guid.NewGuid().ToString();
                clusters[assignedId] = new ClusterState(
                  (float[])data.Embedding.Clone(),
                  1,
                  data.FilePath,
                  data.BoundingBox);
            }

            // Build FaceIndex inline
            _persistenceService.FaceIndex.AddOrUpdate(
                data.FilePath,
                [assignedId],
                (_, existing) => existing.Contains(assignedId)
                    ? existing
                    : [.. existing, assignedId]);

            progress?.Report(new FaceIndexingProgress(i + 1, embeddings.Count, 2, "Clustering Faces"));
        }

        foreach (var (id, s) in clusters)
        {
            _persistenceService.FaceClusters[id] = new ClusterData(
                s.Centroid,
                s.RepFilePath,
                s.RepBoundingBox,
                null);
        }
    }
}
