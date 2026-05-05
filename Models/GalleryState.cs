using Gallery2.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Gallery2.Models;

public partial class GalleryState : ObservableObject
{
    public ObservableCollection<string> ImportedFolders { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageSizePixels))]
    private ThumbnailSize _selectedThumbnailSize = ThumbnailSize.Medium;

    [ObservableProperty]
    public string? _activeFolder;

    [ObservableProperty]
    private GroupMode _selectedGroupMode = GroupMode.Month;

    public int ImageSizePixels => SelectedThumbnailSize switch
    {
        ThumbnailSize.Small => 120,
        ThumbnailSize.Large => 260,
        _ => 180,
    };

    private readonly PersistenceService _persistenceService;

    public GalleryState(PersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
        ImportedFolders.CollectionChanged += OnImportedFoldersChanged;
    }


    public void AddFolder(string path)
    {
        ActiveFolder = path;
        if (!ImportedFolders.Contains(path))
            ImportedFolders.Add(path);
    }

    private void OnImportedFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _persistenceService.SaveFolders(ImportedFolders);
    }
}

public enum GroupMode
{
    None,
    Day,
    Week,
    Month
}

public enum ThumbnailSize
{
    Small,
    Medium,
    Large
}
