using app.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Globalization;

namespace app.Services
{
    public record FolderData(
        Dictionary<string, ImageMetadata> ImageDataCache,
        List<string> ImagePaths,
        Dictionary<DateTime, List<string>> DayGroups,
        Dictionary<(int year, int week), List<string>> WeekGroups,
        Dictionary<(int year, int month), List<string>> MonthGroups,
        List<DateTime> DayOrder,
        List<(int year, int week)> WeekOrder,
        List<(int year, int month)> MonthOrder);

    public static class ImageLoadingService
    {
        public static async Task<FolderData> LoadFolderAsync(string folderPath, IProgress<double>? progress = null)
        {
            var files = System.IO.Directory.GetFiles(folderPath)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var metadataCache = MetadataCacheService.Load();

            var cache = await Task.Run(() =>
            {
                var local = new Dictionary<string, ImageMetadata>();
                bool cacheUpdated = false;

                for (int i = 0; i < files.Count; i++)
                {
                    if (metadataCache.TryGetValue(files[i], out var cached))
                    {
                        local[files[i]] = cached;
                    }
                    else
                    {
                        local[files[i]] = ReadMetadata(files[i]);
                        metadataCache[files[i]] = local[files[i]];
                        cacheUpdated = true;
                    }

                    ThumbnailService.GenerateThumbnail(files[i]);
                    progress?.Report((double)(i + 1) / files.Count);
                }

                if (cacheUpdated)
                    MetadataCacheService.Save(metadataCache);

                return local;
            });

            var paths = cache.OrderBy(x => x.Value.DateTime).Select(x => x.Key).ToList();

            var dayGroups   = new Dictionary<DateTime, List<string>>();
            var weekGroups  = new Dictionary<(int, int), List<string>>();
            var monthGroups = new Dictionary<(int, int), List<string>>();

            foreach (var path in paths)
            {
                var date = cache[path].DateTime?.Date ?? DateTime.MinValue.Date;

                AddToGroup(dayGroups, date, path);
                AddToGroup(weekGroups, (date.Year, GetWeekNumber(date)), path);
                AddToGroup(monthGroups, (date.Year, date.Month), path);
            }

            return new FolderData(
                cache,
                paths,
                dayGroups,
                weekGroups,
                monthGroups,
                dayGroups.Keys.OrderBy(d => d).ToList(),
                weekGroups.Keys.OrderBy(d => d).ToList(),
                monthGroups.Keys.OrderBy(d => d).ToList());
        }

        public static ImageMetadata ReadMetadata(string imagePath)
        {
            var metadata    = new ImageMetadata { FilePath = imagePath };
            var directories = ImageMetadataReader.ReadMetadata(imagePath);

            var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exif?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) ?? false)
                metadata.DateTime = dt;

            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gps?.TryGetGeoLocation(out var geo) ?? false)
                metadata.Location = new GpsCoordinates { Latitude = geo.Latitude, Longitude = geo.Longitude };

            if (gps?.TryGetDouble(GpsDirectory.TagImgDirection, out double dir) ?? false)
            {
                metadata.ImageDirection = dir;
                metadata.IsMagneticDirection = gps.GetString(GpsDirectory.TagImgDirectionRef) == "M";
            }

            return metadata;
        }

        public static int GetWeekNumber(DateTime date) =>
            CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

        private static void AddToGroup<TKey>(Dictionary<TKey, List<string>> dict, TKey key, string path)
            where TKey : notnull
        {
            if (!dict.TryGetValue(key, out var list))
                dict[key] = list = new List<string>();
            list.Add(path);
        }
    }
}
