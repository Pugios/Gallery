using Gallery2.Models;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gallery2.Services;

public class PersistenceService
{
    private static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gallery");
    private static readonly string FoldersFile = Path.Combine(AppDataDir, "folders.txt");
    private static readonly string MetadataCacheFile = Path.Combine(AppDataDir, "metadata_cache.csv");

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

    public Dictionary<string, CachedFileMetadata> LoadMetadataCache()
    {
        if (!File.Exists(MetadataCacheFile)) return [];

        var result = new Dictionary<string, CachedFileMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(MetadataCacheFile))
        {
            var parts = line.Split('|');
            if (parts.Length != 4) continue;
            DateTime? dateTaken = DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
            double? lat = double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var la) ? la : null;
            double? lng = double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var lo) ? lo : null;
            result[parts[0]] = new CachedFileMetadata(parts[0], dateTaken, lat, lng);
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
            $"{e.Longitude?.ToString(CultureInfo.InvariantCulture) ?? ""}");
        File.WriteAllLines(MetadataCacheFile, lines);
    }
}