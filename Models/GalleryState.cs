using Gallery2.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;

namespace Gallery2.Models;

public partial class GalleryState : ObservableObject
{
    private readonly PersistenceService _persistenceService;

    public ObservableCollection<string> ImportedFolders { get; } = [];
    public string? ActiveFolder { get; set; }

    [ObservableProperty] 
    private string _galleryTitle = "Gallery";
    [ObservableProperty] 
    private int _photoCount;
    [ObservableProperty] 
    private int _videoCount;

    public GalleryState(PersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
        ImportedFolders.CollectionChanged += OnImportedFoldersChanged;
    }

    private void OnImportedFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _persistenceService.SaveFolders(ImportedFolders);
    }
}
