using Gallery2.Models;
using Gallery2.Services;
using OpenCvSharp.ML;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gallery2.ViewModels.Windows;

public partial class ImageWindowViewModel : ObservableObject
{
    private readonly PersistenceService _persistenceService;

    [ObservableProperty] private BitmapSource? _imageSource;
    [ObservableProperty] private string _title = "";

    [ObservableProperty] private double _naturalWidth = 1;
    [ObservableProperty] private double _naturalHeight = 1;

    public List<Rect> FaceRects { get; private set; } = [];

    public ImageWindowViewModel(PersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
    }

    public void Load(PictureItem picture)
    {
        Title = Path.GetFileName(picture.FilePath);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(picture.FilePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.EndInit();
        bitmap.Freeze();

        // Rotate the image according to the EXIF orientation
        var transform = new RotateTransform(picture.Rotation ?? 0);
        transform.Freeze();
        var rotated = new TransformedBitmap(bitmap, transform);
        rotated.Freeze();

        ImageSource = rotated;

        NaturalWidth = ImageSource.PixelWidth;
        NaturalHeight = ImageSource.PixelHeight;

        FaceRects = _persistenceService.FaceEmbeddings
            .Where(e => string.Equals(e.FilePath, picture.FilePath, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.BoundingBox)
            .ToList();
    }
}