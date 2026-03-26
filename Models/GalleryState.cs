using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Gallery2.Models;

public partial class GalleryState : ObservableObject
{
    public ObservableCollection<string> ImportedFolders { get; } = [];
    public string? ActiveFolder { get; set; }

    [ObservableProperty] 
    private string _galleryTitle = "Gallery";
    [ObservableProperty] 
    private int _photoCount;
    [ObservableProperty] 
    private int _videoCount;
}
