using Gallery2.Models;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Diagnostics.PerformanceData;
using System.Globalization;
using System.IO;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Gallery2.Services;

public class PersistenceService
{
    private static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gallery");
    private static readonly string FoldersFile = Path.Combine(AppDataDir, "folders.txt");
    private static readonly string MetadataCacheFile = Path.Combine(AppDataDir, "metadata_cache.csv");

    private static readonly string FaceIndexFile = Path.Combine(AppDataDir, "face_index.json");
    private static readonly string FaceClusterFile = Path.Combine(AppDataDir, "face_cluster.json");
    private static readonly string FaceEmbeddingsFile = Path.Combine(AppDataDir, "face_embeddings.csv");

    public ConcurrentDictionary<string, CachedFileMetadata> MetadataCache => _metadataCache.Value;
    private readonly Lazy<ConcurrentDictionary<string, CachedFileMetadata>> _metadataCache;
    public ConcurrentDictionary<string, string[]> FaceIndex => _faceIndex.Value;
    private readonly Lazy<ConcurrentDictionary<string, string[]>> _faceIndex;

    public ConcurrentDictionary<string, ClusterData> FaceClusters => _faceClusters.Value;
    private readonly Lazy<ConcurrentDictionary<string, ClusterData>> _faceClusters;

    public List<EmbeddingData> FaceEmbeddings => _faceEmbeddings.Value;
    private readonly Lazy<List<EmbeddingData>> _faceEmbeddings;


    public PersistenceService()
    {
        _metadataCache = new Lazy<ConcurrentDictionary<string, CachedFileMetadata>>(LoadMetadataCache);
        _faceIndex = new Lazy<ConcurrentDictionary<string, string[]>>(LoadFaceIndex);
        _faceClusters = new Lazy<ConcurrentDictionary<string, ClusterData>>(LoadFaceClusters);
        _faceEmbeddings = new Lazy<List<EmbeddingData>>(LoadFaceEmbeddings);

    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Folders - one folder path per line
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    public List<string> LoadFolders()
    {
        if (!File.Exists(FoldersFile)) return [];

        return [.. File.ReadAllLines(FoldersFile)
              .Where(l => !string.IsNullOrWhiteSpace(l) && Directory.Exists(l))];
    }

    public void SaveFolders(IEnumerable<string> folders)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllLines(FoldersFile, folders);
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Metadata - Key: file path, Value: date taken, lat, lng, rotation
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    public ConcurrentDictionary<string, CachedFileMetadata> LoadMetadataCache()
    {
        if (!File.Exists(MetadataCacheFile)) return [];

        var result = new ConcurrentDictionary<string, CachedFileMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(MetadataCacheFile))
        {
            var parts = line.Split('|');
            if (parts.Length != 5) continue;

            DateTime? dateTaken = DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
            double? lat = double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var la) ? la : null;
            double? lng = double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var lo) ? lo : null;
            int? rotation = int.TryParse(parts[4], out var rot) ? rot : null;

            result[parts[0]] = new CachedFileMetadata(parts[0], dateTaken, lat, lng, rotation);
        }
        return result;
    }

    public void SaveMetadataCache(IEnumerable<CachedFileMetadata> entries)
    {
        Directory.CreateDirectory(AppDataDir);

        var lines = entries.Select(e =>
            $"{e.FilePath}|" +
            $"{e.DateTaken?.ToString("O", CultureInfo.InvariantCulture) ?? ""}|" +
            $"{e.Latitude?.ToString(CultureInfo.InvariantCulture) ?? ""}|" +
            $"{e.Longitude?.ToString(CultureInfo.InvariantCulture) ?? ""}|" +
            $"{e.Rotation?.ToString(CultureInfo.InvariantCulture) ?? ""}");
        File.WriteAllLines(MetadataCacheFile, lines);
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Face Indexing
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // face_embeddings(embeddingData[]) : FilePath(string), BoundingBox(Rect), Embeddings(float[]), Confidence(float)
    // List of Faces and their Embeddings
    private List<EmbeddingData> LoadFaceEmbeddings()
    {
        if (!File.Exists(FaceEmbeddingsFile)) return [];

        var result = new List<EmbeddingData>();
        foreach (var line in File.ReadAllLines(FaceEmbeddingsFile))
        {
            var parts = line.Split('|');
            if (parts.Length != 7) continue;

            if (!int.TryParse(parts[1], out var x) ||
                !int.TryParse(parts[2], out var y) ||
                !int.TryParse(parts[3], out var w) ||
                !int.TryParse(parts[4], out var h) ||
                !float.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var confidence))
                continue;

            var embeddingParts = parts[5].Split(';');
            int embLen = embeddingParts.Length;
            if (embLen == 0) continue;

            var embedding = new float[embLen];
            var valid = true;
            for (int i = 0; i < embLen; i++)
            {
                if (!float.TryParse(embeddingParts[i], NumberStyles.Any, CultureInfo.InvariantCulture, out embedding[i]))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid) continue;

            result.Add(new EmbeddingData(
                parts[0],
                new Rect(x, y, w, h),
                embedding,
                confidence));
        }
        return result;
    }
    public void SaveFaceEmbeddings()
    {
        Directory.CreateDirectory(AppDataDir);

        var lines = FaceEmbeddings.Select(e =>
            $"{e.FilePath}|" +
            $"{e.BoundingBox.X}|" +
            $"{e.BoundingBox.Y}|" +
            $"{e.BoundingBox.Width}|" +
            $"{e.BoundingBox.Height}|" +
            string.Join(";", e.Embedding
                .Select(f => f.ToString(CultureInfo.InvariantCulture))) + "|" +
            e.Confidence.ToString(CultureInfo.InvariantCulture));

        File.WriteAllLines(FaceEmbeddingsFile, lines);
    }

    // =====================================================
    // face_clusters(Dictionary<string, clusterData>) : cluster ID: {Name(string?), Representative Thumbnail File Path, Representative Thumbnail Bounding Box, Centroid
    private ConcurrentDictionary<string, ClusterData> LoadFaceClusters()
    {
        if (!File.Exists(FaceClusterFile))
            return new ConcurrentDictionary<string, ClusterData>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(FaceClusterFile);
        var data = JsonSerializer.Deserialize<Dictionary<string, ClusterData>>(json);

        return data is null
            ? new ConcurrentDictionary<string, ClusterData>(StringComparer.OrdinalIgnoreCase)
            : new ConcurrentDictionary<string, ClusterData>(data, StringComparer.OrdinalIgnoreCase);
    }

    public void SaveFaceClusters()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(FaceClusterFile,
            JsonSerializer.Serialize(FaceClusters, new JsonSerializerOptions { WriteIndented = true }));
    }

    // =====================================================
    // face_index (Dictionary<string, string[]>): key is FilePath and string[] is [cluster IDs]
    private ConcurrentDictionary<string, string[]> LoadFaceIndex()
    {
        if (!File.Exists(FaceIndexFile))
            return new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(FaceIndexFile);
        var data = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);

        return data is null
            ? new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            : new ConcurrentDictionary<string, string[]>(data, StringComparer.OrdinalIgnoreCase);
    }

    public void SaveFaceIndex()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(FaceIndexFile,
            JsonSerializer.Serialize(FaceIndex, new JsonSerializerOptions { WriteIndented = true }));
    }



}