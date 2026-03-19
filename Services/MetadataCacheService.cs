using app.Models;
using System.Text.Json;

namespace app.Services
{
    public static class MetadataCacheService
    {
        private static readonly string FilePath =
            Path.Combine(FileSystem.AppDataDirectory, "metadata_cache.json");

        public static Dictionary<string, ImageMetadata> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, ImageMetadata>>(
                        File.ReadAllText(FilePath));
                    if (dict != null) return dict;
                }
            }
            catch { }
            return new Dictionary<string, ImageMetadata>();
        }

        public static void Save(Dictionary<string, ImageMetadata> cache)
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(cache));
            }
            catch { }
        }
    }
}
