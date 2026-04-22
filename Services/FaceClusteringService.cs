using Gallery2.Models;

namespace Gallery2.Services;

public class FaceClusteringService
{
    private readonly PersistenceService _persistenceService;

    // Agglomerative (UPGMA) threshold: two clusters are merged only when their
    // average-linkage cosine similarity is at or above this value.
    // Raise  → fewer, tighter clusters (may split same person across angles).
    // Lower  → more liberal merges (may join different people who look similar).
    private const float MergeThreshold = 0.30f;

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

    /// <summary>
    /// Removes a cluster and greedily re-assigns its faces to the best remaining cluster
    /// (by cosine similarity to that cluster's centroid). Faces that don't meet
    /// <see cref="MergeThreshold"/> against any remaining cluster are left unassigned
    /// but stay in <see cref="PersistenceService.FaceIndex"/> with an empty entry so
    /// incremental indexing does not re-process them.
    /// </summary>
    public void DissolveCluster(string dissolvedId)
    {
        if (!_persistenceService.FaceClusters.TryRemove(dissolvedId, out var dissolved))
            return;

        // Files whose FaceIndex entry contained the dissolved cluster
        var affectedFiles = _persistenceService.FaceIndex
            .Where(kvp => kvp.Value.Contains(dissolvedId, StringComparer.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();

        // Snapshot remaining clusters so we search a stable set
        var remaining = _persistenceService.FaceClusters.ToArray();

        foreach (string filePath in affectedFiles)
        {
            // Strip the dissolved ID from this file's cluster list
            _persistenceService.FaceIndex.AddOrUpdate(
                filePath,
                [],
                (_, existing) => existing
                    .Where(id => !string.Equals(id, dissolvedId, StringComparison.OrdinalIgnoreCase))
                    .ToArray());

            if (remaining.Length == 0) continue;

            // In multi-face images the file has several embeddings.
            // The one most similar to the dissolved centroid is the one we're reassigning.
            var candidate = _persistenceService.FaceEmbeddings
                .Where(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .MaxBy(e => CosineSimilarity(e.Embedding, dissolved.Centroid));

            if (candidate is null) continue;

            // Find the best remaining cluster for this embedding
            string? bestId   = null;
            float   bestSim  = MergeThreshold;          // strict floor — same as clustering
            foreach (var (id, data) in remaining)
            {
                float s = CosineSimilarity(candidate.Embedding, data.Centroid);
                if (s > bestSim) { bestSim = s; bestId = id; }
            }

            if (bestId is null) continue;               // no suitable cluster — leave unassigned

            _persistenceService.FaceIndex.AddOrUpdate(
                filePath,
                [bestId],
                (_, existing) => existing.Contains(bestId, StringComparer.OrdinalIgnoreCase)
                    ? existing
                    : [.. existing, bestId]);
        }
    }

    public void ClusterFaces(IProgress<FaceIndexingProgress>? progress = null)
    {
        List<EmbeddingData> embeddings = _persistenceService.FaceEmbeddings;
        if (embeddings.Count == 0) return;

        _persistenceService.FaceClusters.Clear();
        _persistenceService.FaceIndex.Clear();

        int n = embeddings.Count;
        int embLen = embeddings[0].Embedding.Length;

        // ── Phase 1: build full pairwise cosine-similarity matrix ──────────────
        // sim[i][j] starts as face-to-face similarity and is updated in-place
        // via the Lance-Williams formula whenever two clusters merge.
        float[][] sim = new float[n][];
        for (int i = 0; i < n; i++)
        {
            sim[i] = new float[n];
            sim[i][i] = 1f;
            for (int j = 0; j < i; j++)
            {
                float s = CosineSimilarity(embeddings[i].Embedding, embeddings[j].Embedding);
                sim[i][j] = s;
                sim[j][i] = s;
            }
            progress?.Report(new FaceIndexingProgress(i + 1, n * 2, "Computing Similarities"));
        }

        // ── Phase 2: UPGMA agglomerative merging ───────────────────────────────
        var count   = new int[n];           // live member count per cluster
        var active  = new bool[n];          // false once a cluster is absorbed
        var members = new List<int>[n];     // embedding indices belonging to each cluster
        for (int i = 0; i < n; i++)
        {
            count[i]   = 1;
            active[i]  = true;
            members[i] = [i];
        }

        int mergesDone = 0;
        while (true)
        {
            // Find the active pair with the highest similarity (≥ threshold to qualify)
            int   bestA   = -1, bestB = -1;
            float bestSim = MergeThreshold;   // acts as a hard floor

            for (int a = 0; a < n; a++)
            {
                if (!active[a]) continue;
                for (int b = a + 1; b < n; b++)
                {
                    if (!active[b]) continue;
                    if (sim[a][b] > bestSim) { bestSim = sim[a][b]; bestA = a; bestB = b; }
                }
            }

            if (bestA < 0) break;   // no pair qualifies — we're done

            // Merge bestB into bestA.
            // Update every remaining cluster C's similarity to bestA using
            // the Lance-Williams UPGMA formula:
            //   sim(A∪B, C) = (|A|·sim(A,C) + |B|·sim(B,C)) / (|A|+|B|)
            int cntA = count[bestA], cntB = count[bestB];
            for (int c = 0; c < n; c++)
            {
                if (!active[c] || c == bestA || c == bestB) continue;
                float updated = (cntA * sim[bestA][c] + cntB * sim[bestB][c]) / (float)(cntA + cntB);
                sim[bestA][c] = sim[c][bestA] = updated;
            }

            members[bestA].AddRange(members[bestB]);
            count[bestA]  = cntA + cntB;
            active[bestB] = false;

            mergesDone++;
            progress?.Report(new FaceIndexingProgress(n + mergesDone, n * 2, "Clustering Faces"));
        }

        // ── Phase 3: build FaceIndex and FaceClusters ─────────────────────────
        foreach (int c in Enumerable.Range(0, n).Where(i => active[i]))
        {
            // Mean centroid over all member embeddings
            var centroid = new float[embLen];
            foreach (int idx in members[c])
                for (int j = 0; j < embLen; j++)
                    centroid[j] += embeddings[idx].Embedding[j];
            for (int j = 0; j < embLen; j++)
                centroid[j] /= members[c].Count;

            // Representative: the member whose embedding is closest to the centroid
            int repIdx = members[c].MaxBy(i => CosineSimilarity(embeddings[i].Embedding, centroid));
            var rep    = embeddings[repIdx];

            string clusterId = Guid.NewGuid().ToString();
            _persistenceService.FaceClusters[clusterId] = new ClusterData(
                centroid, rep.FilePath, rep.BoundingBox, null);

            foreach (int idx in members[c])
            {
                string file = embeddings[idx].FilePath;
                _persistenceService.FaceIndex.AddOrUpdate(
                    file,
                    [clusterId],
                    (_, existing) => existing.Contains(clusterId)
                        ? existing
                        : [.. existing, clusterId]);
            }
        }
    }
}
