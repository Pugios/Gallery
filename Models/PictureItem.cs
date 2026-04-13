using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media.Imaging;

namespace Gallery2.Models;

public partial class PictureItem : ObservableObject
{
    public DateTime? DateTaken { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int? Rotation { get; init; }

    public string FilePath { get; }

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    public PictureItem(string filePath)
    {
        FilePath = filePath;
    }

    public PictureItem(CachedFileMetadata meta) : this(meta.FilePath)
    {
        DateTaken = meta.DateTaken;
        Latitude = meta.Latitude;
        Longitude = meta.Longitude;
        Rotation = meta.Rotation;
    }
}

public partial class FacePictureItem : ObservableObject
{
    public string ClusterId { get; init; } = "";
    public string Name { get; init; } = "";

    [ObservableProperty]
    private BitmapSource? _thumbnail;
}

