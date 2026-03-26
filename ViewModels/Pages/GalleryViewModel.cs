using Gallery2.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Directory = System.IO.Directory;

namespace Gallery2.ViewModels.Pages;

public partial class GalleryViewModel : ObservableObject, INavigationAware
{
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Fields and Properties
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

    // The GalleryState service holds the list of imported folders and the currently active folder.
    private readonly GalleryState _galleryState;
    private readonly HeaderState _headerState;

    // List of Pictures on this Page
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPictures))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private ObservableCollection<PictureItem> _pictures = [];

    // True when the app is currently loading a folder of pictures
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isLoading;
    [ObservableProperty]
    private double _loadingProgress;

    // True when there is something to show in the grid
    public bool HasPictures => Pictures.Count > 0;

    // True when the empty-state prompt should be visible
    public bool ShowEmptyState => !HasPictures && !IsLoading;

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Constructor with GalleryState injected
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    public GalleryViewModel(GalleryState galleryState, HeaderState headerState)
    {
        _galleryState = galleryState;
        _headerState = headerState;
    }

    // INavigationAware Components
    public async Task OnNavigatedToAsync()
    {
        if (_galleryState.ActiveFolder is null)
        {
            if (_galleryState.ImportedFolders.Count > 0)
                await LoadFoldersAsync(_galleryState.ImportedFolders);
            else
                _headerState.IsVisible = false;
        }
        else
        {
            await LoadFoldersAsync([_galleryState.ActiveFolder]);
        }
    }
    public Task OnNavigatedFromAsync()
    {
        _headerState.IsVisible = false;
        return Task.CompletedTask;
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Selecting and Loading Pictures
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

    [RelayCommand]
    private async Task OnAddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder of images" };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        _galleryState.ActiveFolder = path;

        if (!_galleryState.ImportedFolders.Contains(path))
            _galleryState.ImportedFolders.Add(path);
    }

    private async Task LoadFoldersAsync(IEnumerable<string> folderPaths)
    {
        IsLoading = true;
        Pictures.Clear();
        await Dispatcher.Yield(DispatcherPriority.Background);

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                  { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".mp4", ".mov", ".avi", ".mkv" };

        // Scan the folder on a background thread so the UI stays responsive
        List<PictureItem> tempPictures = new();

        var progress = new Progress<double>(value => LoadingProgress = value);

        await Task.Run(() =>
        {
            // Get total Count of Files for ProgressBar
            var allFiles = new List<string>();
            foreach (var folderPath in folderPaths)
            {
                allFiles.AddRange(
                    Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f))));
            }

            int total = allFiles.Count;
            int processed = 0;

            foreach (var folderPath in folderPaths)
            {
                // Get all files with the specified extensions, including subfolders
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f)));

                // Add all items immediately so the grid appears right away (thumbnails load after)
                foreach (var file in files)
                {
                    // Read Metadata
                    var directories = ImageMetadataReader.ReadMetadata(file);

                    // Date taken
                    var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                    DateTime? dateTaken = null;
                    if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                        dateTaken = dt;

                    // GPS
                    var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
                    var location = gps?.GetGeoLocation();   // returns null if no GPS data
                    double? lat = location?.Latitude;
                    double? lng = location?.Longitude;

                    tempPictures.Add(new PictureItem(file)
                    {
                        DateTaken = dateTaken,
                        Latitude = lat,
                        Longitude = lng
                    });

                    if (total > 0)
                        ((IProgress<double>)progress).Report(++processed * 100.0 / total);
                }
            }
        });

        Pictures = new ObservableCollection<PictureItem>(tempPictures);

        // Set Header
        _headerState.Icon = SymbolRegular.Image24;
        _headerState.Title = _galleryState.ActiveFolder is not null
            ? Path.GetFileName(_galleryState.ActiveFolder)
            : "Gallery";

        int picCount = Pictures.Count(p => !IsVideo(p.FilePath));
        int vidCount = Pictures.Count(p => IsVideo(p.FilePath));
        _headerState.Subtitle = "";
        _headerState.Subtitle += picCount > 0 ? $"{picCount} photo{(picCount > 1 ? "s" : "")}" : "";
        _headerState.Subtitle += vidCount > 0 ? $"{(picCount > 0 ? " · " : "")}{vidCount} video{(vidCount > 1 ? "s" : "")}" : "";
        _headerState.IsVisible = true;

        // Load thumbnails concurrently, 4 at a time
        IsLoading = false;
        using var semaphore = new SemaphoreSlim(4);
        var tasks = Pictures.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                item.Thumbnail = await Task.Run(() => LoadThumbnail(item.FilePath));
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private static bool IsVideo(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".mov" or ".avi" or ".mkv";
    }

    private static BitmapSource? LoadThumbnail(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.DecodePixelWidth = 200;           // decode at thumbnail size, not full res
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();                         // required to pass across threads safely
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
