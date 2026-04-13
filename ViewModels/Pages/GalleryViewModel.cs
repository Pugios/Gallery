using Gallery2.Helpers;
using Gallery2.Models;
using Gallery2.Services;
using Gallery2.ViewModels.Header;
using Gallery2.Views.Windows;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
    // Headerstate to change the header from this page (title, subtitle, icon)
    private readonly HeaderState _headerState;
    private readonly GalleryHeaderViewModel _galleryHeader;
    // PersistenceService to load/save folders and metadata
    private readonly PersistenceService _persistenceService;
    // Service to load Windows Shell thumbnails
    private readonly ShellThumbnailService _shellThumbnailService;
    private readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);

    // Run Face Indexing after Loading a folder
    private readonly FaceIndexingService _faceIndexingService;

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

    // Pictures CollectionView to change layout and grouping without changing the underlying collection
    public ICollectionView? PicturesView { get; private set; }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Constructor with GalleryState injected
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    public GalleryViewModel(GalleryState galleryState, HeaderState headerState, PersistenceService persistenceService, ShellThumbnailService shellThumbnailService, FaceIndexingService faceIndexingService)
    {
        _galleryState = galleryState;
        _headerState = headerState;
        _persistenceService = persistenceService;
        _shellThumbnailService = shellThumbnailService;
        _faceIndexingService = faceIndexingService;
        _galleryHeader = new GalleryHeaderViewModel(galleryState);

        _galleryState.PropertyChanged += OnToolbarStateChanged;
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
        _galleryState.AddFolder(dialog.FolderName);
    }

    private async Task LoadFoldersAsync(IEnumerable<string> folderPaths)
    {
        IsLoading = true;
        Pictures.Clear();
        await Dispatcher.Yield(DispatcherPriority.Background);

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".mp4", ".mov", ".avi", ".mkv" };

        // Scan the folder on a background thread so the UI stays responsive
        List<PictureItem> tempPictures = new();
        var progress = new Progress<double>(value => LoadingProgress = value);

        var cache = _persistenceService.MetadataCache;
        bool cacheUpdated = false;

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

            // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
            // Metadata
            // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
            foreach (var file in allFiles)
            {
                DateTime? dateTaken = null;
                double? lat = null;
                double? lng = null;
                int? rotation = null;

                if (cache.TryGetValue(file, out var cached))
                {
                    dateTaken = cached.DateTaken;
                    lat = cached.Latitude;
                    lng = cached.Longitude;
                    rotation = cached.Rotation;
                }
                else
                {
                    var directories = ImageMetadataReader.ReadMetadata(file);

                    // Date taken
                    var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                    if (subIfd != null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                        dateTaken = dt;

                    // GPS
                    var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
                    var location = gps?.GetGeoLocation();   // returns null if no GPS data
                    lat = location?.Latitude;
                    lng = location?.Longitude;

                    // Rotation
                    var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                    var rot = (ifd0 != null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out int o) ? o : 0) switch
                    {
                        3 => 180,
                        6 => 90,
                        8 => 270,
                        _ => 0
                    };
                    rotation = rot;

                    cache[file] = new CachedFileMetadata(file, dateTaken, lat, lng, rot);
                    cacheUpdated = true;
                }

                tempPictures.Add(new PictureItem(cache[file]));

                if (total > 0)
                    ((IProgress<double>)progress).Report(++processed * 100.0 / total);
            }
        });

        if (cacheUpdated)
        {
            _persistenceService.SaveMetadataCache(cache.Values);
            // Start Facial Recognition if there was new images!
            _ = _faceIndexingService.IndexUnprocessedAsync();
        }

        Pictures = new ObservableCollection<PictureItem>(tempPictures.OrderBy(p => p.DateTaken ?? DateTime.MaxValue));
        PicturesView = CollectionViewSource.GetDefaultView(Pictures);
        OnPropertyChanged(nameof(PicturesView));

        ApplyGrouping();
        UpdateHeader();
        IsLoading = false;

        // Load Thumbnails
        using var semaphore = new SemaphoreSlim(4);
        var tasks = Pictures.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                item.Thumbnail = await Task.Run(() => LoadThumbnail(item));
            }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private void UpdateHeader()
    {
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
        _headerState.HeaderContent = _galleryHeader;
    }

    private static bool IsVideo(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".mov" or ".avi" or ".mkv";
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Load Thumbnails
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

    // Load weak references to each Thumbnail. (let garbage collector remove thumbnails if memory under pressure)
    private BitmapSource? LoadThumbnail(PictureItem picture)
    {
        if (_thumbnailCache.TryGetValue(picture.FilePath, out var weakRef) && weakRef.TryGetTarget(out var cached))
            return cached;

        var shellResult = _shellThumbnailService.GetThumbnail(picture.FilePath, 500);
        if (shellResult is not null)
        {
            _thumbnailCache[picture.FilePath] = new WeakReference<BitmapSource>(shellResult);
            return shellResult;
        }

        var fallback = LoadThumbnailFallback(picture);
        _thumbnailCache[picture.FilePath] = new WeakReference<BitmapSource>(fallback);
        return fallback;
    }

    private static BitmapSource? LoadThumbnailFallback(PictureItem picture)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(picture.FilePath);
            bitmap.DecodePixelWidth = 500;           // decode at thumbnail size, not full res
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();

            // Rotate the image according to the EXIF orientation
            var transform = new RotateTransform(picture.Rotation ?? 0);
            transform.Freeze();
            var rotated = new TransformedBitmap(bitmap, transform);
            rotated.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Grouping
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

    private void OnToolbarStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryState.SelectedGroupMode))
        {
            ApplyGrouping();
        }
    }

    private void ApplyGrouping()
    {
        if (PicturesView is null) return;

        PicturesView.GroupDescriptions.Clear();

        if (_galleryState.SelectedGroupMode == GroupMode.None) return;

        var converter = new DateGroupConverter { Mode = _galleryState.SelectedGroupMode };
        PicturesView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(PictureItem.DateTaken), converter));
    }
}