using System.Text.Json;

namespace app.Services
{
    public static class LikeService
    {
        private static readonly string FilePath =
            Path.Combine(FileSystem.AppDataDirectory, "likes.json");

        public static HashSet<string> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
                    if (paths != null) return new HashSet<string>(paths);
                }
            }
            catch { }
            return new HashSet<string>();
        }

        public static void Save(HashSet<string> likedPaths)
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(likedPaths.ToList()));
            }
            catch { }
        }
    }
}
