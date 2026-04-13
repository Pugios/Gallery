using Gallery2.Models;
using Gallery2.Services;
using Gallery2.ViewModels.Header;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace Gallery2.ViewModels.Pages;

public partial class SearchViewModel : ObservableObject, INavigationAware
{
    private readonly HeaderState _headerState;
    private readonly FaceIndexingService _faceIndexingService;
    private readonly PersistenceService _persistenceService;
    private readonly ShellThumbnailService _shellThumbnailService;

    // Indexing progress
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotIndexing))]
    private bool _isIndexing;
    public bool IsNotIndexing => !IsIndexing;

    [ObservableProperty]
    private string _stepText = "";

    [ObservableProperty]
    private double _progress;

    // People Selector 
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [ObservableProperty]
    private ObservableCollection<FacePictureItem> _people = [];

    // Gallery
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [ObservableProperty]
    private ObservableCollection<PictureItem> _pictures = [];

    private readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SearchHeaderViewModel _searchHeader;

    public SearchViewModel(HeaderState headerState, FaceIndexingService faceIndexingService, PersistenceService persistenceService, ShellThumbnailService shellThumbnailService)
    {
        _headerState = headerState;
        _faceIndexingService = faceIndexingService;
        _persistenceService = persistenceService;
        _shellThumbnailService = shellThumbnailService;
        _searchHeader = new SearchHeaderViewModel(ReindexFacesCommand);
    }

    public Task OnNavigatedToAsync()
    {
        UpdateHeader();
        _ = LoadFacesAsync();
        return Task.CompletedTask;
    }

    private void UpdateHeader()
    {
        _headerState.Icon = SymbolRegular.Search24;
        _headerState.Title = "Search";
        _headerState.Subtitle = "";
        _headerState.IsVisible = true;
        _headerState.HeaderContent = _searchHeader;
    }

    public Task OnNavigatedFromAsync()
    {
        _headerState.HeaderContent = null;
        return Task.CompletedTask;
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Index Images
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [RelayCommand]
    private async Task ReindexFaces()
    {
        IsIndexing = true;
        _searchHeader.IsIndexing = true;

        var progress = new Progress<FaceIndexingProgress>(update =>
        {
            StepText = update.Title;
            Progress = update.TotalFiles > 0 ? (update.ProcessedFiles / (double)update.TotalFiles) * 100.0 : 0;
        });

        await _faceIndexingService.DeleteIndex(progress);
        await _faceIndexingService.IndexUnprocessedAsync(progress);
        await LoadFacesAsync();

        IsIndexing = false;
        _searchHeader.IsIndexing = false;
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Face Selector
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    private async Task LoadFacesAsync()
    {
        var clusters = _persistenceService.FaceClusters;
        if (clusters.IsEmpty) return;

        List<FacePictureItem> people = clusters
            .Select(kvp => new FacePictureItem
            {
                ClusterId = kvp.Key,
                Name = kvp.Value.Name ?? ""
            })
            .ToList();

        People = new ObservableCollection<FacePictureItem>(people);

        await Task.Run(() =>
        {
            foreach (FacePictureItem person in people)
            {
                var cluster = clusters[person.ClusterId];
                int rotation = _persistenceService.MetadataCache.TryGetValue(cluster.RepresentativeFilePath, out var meta)
                    ? meta.Rotation ?? 0 : 0;
                person.Thumbnail = LoadFaceThumbnail(cluster.RepresentativeFilePath, cluster.RepresentativeBoundingBox, rotation);
            }
        });
    }

    private static BitmapSource? LoadFaceThumbnail(string filePath, Rect rect, int rotation)
    {
        try
        {
            // Load at full resolution — face rect coords are in full-res space (no scaling math needed).
            // Only one image per cluster representative is loaded this way, so memory cost is acceptable.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();

            // Apply EXIF rotation so coordinates match Cv2.ImRead's auto-rotated space
            BitmapSource source = bitmap;
            if (rotation != 0)
            {
                var rotated = new TransformedBitmap(bitmap, new RotateTransform(rotation));
                rotated.Freeze();
                source = rotated;
            }

            // Expand the bounding box to show torso context (~2.5× the face size, centered)
            const double padding = 1.5; // extra 1.5× on each side → total ~2.5× the face
            int padX = (int)(rect.Width * padding);
            int padY = (int)(rect.Height * padding);

            int x = (int)Math.Clamp(rect.X - padX, 0, source.PixelWidth - 1);
            int y = (int)Math.Clamp(rect.Y - padY, 0, source.PixelHeight - 1);
            int x2 = (int)Math.Clamp(rect.X + rect.Width + padX, x + 1, source.PixelWidth);
            int y2 = (int)Math.Clamp(rect.Y + rect.Height + padY, y + 1, source.PixelHeight);
            int w = x2 - x;
            int h = y2 - y;

            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            return cropped;
        }
        catch { return null; }
    }

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Filtered Gallery
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [RelayCommand]
    private async Task OnSelectPerson(FacePictureItem person)
    {
        Pictures.Clear();
        // Find every file that contains this person's cluster ID
        var matchingImages = _persistenceService.FaceIndex
            .Where(kvp => kvp.Value.Contains(person.ClusterId, StringComparer.OrdinalIgnoreCase))
            .Select(kvp => _persistenceService.MetadataCache.TryGetValue(kvp.Key, out var meta)
                ? new PictureItem(meta)
                : new PictureItem(kvp.Key))
            .ToList();


        Pictures = new ObservableCollection<PictureItem>(matchingImages);

        // Load thumbnails with max 4 in parallel
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
            bitmap.Freeze();                         // required to pass across threads safely

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
}

