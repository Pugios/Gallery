using Gallery2.Models;
using Gallery2.Services;
using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private FacePictureItem? _selectedPerson;

    // Gallery
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [ObservableProperty]
    private ObservableCollection<PictureItem> _pictures = [];


    public SearchViewModel(HeaderState headerState, FaceIndexingService faceIndexingService, PersistenceService persistenceService, ShellThumbnailService shellThumbnailService)
    {
        _headerState = headerState;
        _faceIndexingService = faceIndexingService;
        _persistenceService = persistenceService;
        _shellThumbnailService = shellThumbnailService;
    }

    public Task OnNavigatedToAsync()
    {
        _headerState.Icon = SymbolRegular.Search24;
        _headerState.Title = "Search";
        _headerState.Subtitle = "";
        _headerState.IsVisible = true;
        _headerState.ShowComboBox = false;

        _ = LoadFacesAsync();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    // Index Images
    // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    [RelayCommand]
    private async Task OnStartIndexing()
    {
        IsIndexing = true;
        int totalSteps = 2;

        var progress = new Progress<FaceIndexingProgress>(update =>
        {
            StepText = update.Title;
            Progress = update.TotalFiles > 0 ? (update.Step - 1 + update.ProcessedFiles / (double)update.TotalFiles) * 100.0 / totalSteps : 0;
        });

        await _faceIndexingService.IndexUnprocessedAsync(progress);
        await LoadFacesAsync();

        IsIndexing = false;
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
                person.Thumbnail = LoadFaceThumbnail(cluster.RepresentativeFilePath, cluster.RepresentativeBoundingBox);
            }
        });
    }

    private static BitmapSource? LoadFaceThumbnail(string filePath, FaceRect rect)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.DecodePixelWidth = 500;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();

            // Bounding box coords are in 500×500 detection space.
            // Scale to the actual decoded pixel size (aspect-ratio-preserved).
            double scaleX = bitmap.PixelWidth / 500.0;
            double scaleY = bitmap.PixelHeight / 500.0;

            int x = (int)(rect.X * scaleX);
            int y = (int)(rect.Y * scaleY);
            int w = Math.Max(1, (int)(rect.Width * scaleX));
            int h = Math.Max(1, (int)(rect.Height * scaleY));

            x = Math.Clamp(x, 0, bitmap.PixelWidth - 1);
            y = Math.Clamp(y, 0, bitmap.PixelHeight - 1);
            w = Math.Min(w, bitmap.PixelWidth - x);
            h = Math.Min(h, bitmap.PixelHeight - y);

            if (w <= 0 || h <= 0) return bitmap;

            var cropped = new CroppedBitmap(bitmap, new Int32Rect(x, y, w, h));
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
        SelectedPerson = person;

        // Find every file that contains this person's cluster ID
        var matchingFiles = _persistenceService.FaceIndex
            .Where(kvp => kvp.Value.Contains(person.ClusterId, StringComparer.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();

        var items = matchingFiles.Select(f => new PictureItem(f)).ToList();
        Pictures = new ObservableCollection<PictureItem>(items);

        // Load thumbnails with max 4 in parallel
        using var semaphore = new SemaphoreSlim(4);
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                item.Thumbnail = await Task.Run(() =>
                    _shellThumbnailService.GetThumbnail(item.FilePath, 500)
                    ?? LoadThumbnailFallback(item.FilePath));
            }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private static BitmapSource? LoadThumbnailFallback(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.DecodePixelWidth = 500;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }
}
