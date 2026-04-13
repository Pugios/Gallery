using Gallery2.Models;

namespace Gallery2.ViewModels.Header;

public class GalleryHeaderViewModel(GalleryState galleryState)
{
    public GalleryState GalleryState { get; } = galleryState;
}
