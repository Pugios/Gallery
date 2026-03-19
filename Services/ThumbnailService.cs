using SkiaSharp;
using System.Security.Cryptography;
using System.Text;

namespace app.Services
{
    internal static class ThumbnailService
    {
        private static readonly string ThumbnailDir =
            Path.Combine(FileSystem.AppDataDirectory, "thumbs");

        private const int ThumbnailSize = 300;

        // Returns the deterministic thumbnail path for an image (may or may not exist yet).
        public static string GetThumbnailPath(string imagePath)
        {
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(imagePath)));
            return Path.Combine(ThumbnailDir, hash + ".jpg");
        }

        // Generates and saves the thumbnail if it doesn't already exist. Returns the thumbnail path.
        // Falls back to the original path if generation fails.
        public static string GenerateThumbnail(string imagePath)
        {
            Directory.CreateDirectory(ThumbnailDir);
            string thumbPath = GetThumbnailPath(imagePath);

            if (File.Exists(thumbPath))
                return thumbPath;

            try
            {
                using var original = SKBitmap.Decode(imagePath);
                if (original == null)
                    return imagePath;

                float scale = Math.Min((float)ThumbnailSize / original.Width, (float)ThumbnailSize / original.Height);
                int newW = Math.Max(1, (int)(original.Width * scale));
                int newH = Math.Max(1, (int)(original.Height * scale));

                using var resized = original.Resize(new SKImageInfo(newW, newH), SKFilterQuality.Medium);
                if (resized == null)
                    return imagePath;

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 75);
                using var output = File.Create(thumbPath);
                data.SaveTo(output);
            }
            catch
            {
                return imagePath;
            }

            return thumbPath;
        }
    }
}
