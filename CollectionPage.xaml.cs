using app;
using app.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SharpHook;
using SharpHook.Providers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;


namespace app
{
    public partial class CollectionPage : ContentPage, IQueryAttributable
    {

        public CollectionPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        // Accept Query Data aka. Folder Path of Images
        protected string folderPath { get; private set; }
        public async void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            IsLoading = true;
            Progress = 0;

            folderPath = Uri.UnescapeDataString(query["folderPath"] as string);

            await LoadImages();
            Progress = 1;
            IsLoading = false;
            showFirstImg();
            updateUI();
            //await UiHook();
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //               Load Images
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        // Loading Animation
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress == value)
                    return;

                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }

        public ObservableCollection<string> imagePaths { get; } = new();    // ordered single-image list
        private Dictionary<string, ImageMetadata> imageDataCache = new();   // Stores metadata for images

        private async Task LoadImages()
        {
            imagePaths.Clear();
            imageDataCache.Clear();

            var files = System.IO.Directory.GetFiles(folderPath)
                             .Where(f =>
                                 f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)// ||
                                                                                       //f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                                                       //f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                 )
                             .ToList();


            // Add files to a collection bound to your CarouselView
            imageDataCache = await Task.Run(() =>
            {
                Dictionary<string, ImageMetadata> localImageDataCache = new Dictionary<string, ImageMetadata>();

                foreach (var file in files)
                {
                    ImageMetadata metadata = readInfo(file);
                    localImageDataCache.Add(file, metadata);

                    double progress = (double)localImageDataCache.Count / files.Count;
                    MainThread.BeginInvokeOnMainThread(() => { Progress = progress; });
                }

                return localImageDataCache;
            });


            foreach (string imagePath in imageDataCache.OrderBy(x => x.Value.DateTime).Select(x => x.Key))
            {
                imagePaths.Add(imagePath);
            }


            return;
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              EXIF Data Reading
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%        
        private ImageMetadata readInfo(string imagePath)
        {
            // Check if we already have the date cached
            if (imageDataCache.TryGetValue(imagePath, out var cachedMetadata))
            {
                return cachedMetadata;
            }

            ImageMetadata metadata = new ImageMetadata { FilePath = imagePath };

            IEnumerable<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(imagePath);

            // Read Date/
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            if (exifSubIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTimeOriginal) ?? false)
            {
                metadata.DateTime = dateTimeOriginal;
            }

            // Read GPS
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            if (gpsDirectory?.TryGetGeoLocation(out var geoLocation) ?? false)
            {
                metadata.Location = new GpsCoordinates()
                {
                    Latitude = geoLocation.Latitude,
                    Longitude = geoLocation.Longitude
                };
            }

            // Image Direction
            if (gpsDirectory?.TryGetDouble(GpsDirectory.TagImgDirection, out double direction) ?? false)
            {
                metadata.ImageDirection = direction;

                // Magnetic Direction
                var directionRef = gpsDirectory.GetString(GpsDirectory.TagImgDirectionRef);
                metadata.IsMagneticDirection = directionRef == "M";
            }

            return metadata;
            /*
            Debug.WriteLine($"Data for {imagePath}");
            foreach (var directory in directories)
                foreach (var tag in directory.Tags)
                {
                    Debug.WriteLine($"Directory {directory.Name} - Tag {tag.Name} = {tag.Description}");
                }
            */
        }
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //          Single Image View
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%% 

        private string _focusedImage = string.Empty;
        public string ImagePath
        {
            get => _focusedImage;
            set
            {
                _focusedImage = value;
                OnPropertyChanged(nameof(ImagePath));
            }
        }

        private int focusedIndex = 0;

        private void showFirstImg()
        {
            if (imagePaths?.Count > 0)
            {
                focusedIndex = 0;
                ImagePath = imagePaths[focusedIndex];
            }
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //          Multi Image View
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%% 

        private List<string> _focusedGroup = new();
        public List<string> GroupImagePaths
        {
            get => _focusedGroup;
            set
            {
                _focusedGroup = value ?? new List<string>();
                OnPropertyChanged(nameof(GroupImagePaths));
            }
        }

        private void UpdateGridLayout(int itemCount)
        {
            var span = Math.Max(1, (int)Math.Round(Math.Sqrt(itemCount)));
            GIL.Span = span;
            Debug.WriteLine($"Grid has span of {span}");
        }

        private List<string> getImagesOfDay()
        {
            var date = imageDataCache[imagePaths[focusedIndex]].DateTime.GetValueOrDefault();
            List<string> imagesOfDay = new List<string>();

            if (imagePaths.Count == 0)
                return imagesOfDay;

            var currDay = imageDataCache[imagePaths[focusedIndex]].DateTime.GetValueOrDefault().Date;

            foreach (var imagePath in imagePaths)
            {
                var imageDate = imageDataCache[imagePath].DateTime.GetValueOrDefault().Date;
                Debug.WriteLine($"Comparing {imageDate} to {currDay}");
                if (imageDate == currDay)
                {
                    imagesOfDay.Add(imagePath);
                }
            }

            Debug.WriteLine($"Found {imagesOfDay.Count} images for day {currDay.ToShortDateString()}");
            return imagesOfDay;
        }

        private void showCurrDay()
        {
            List<string> dayList = getImagesOfDay();

            if (dayList?.Count <= 0)
            {
                return;
            }

            UpdateGridLayout(dayList.Count);
            GroupImagePaths = dayList;
            return;
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //                   UI
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%  

        // Next / Previous Image
        private void OnNextClicked(object sender, EventArgs e)
        {
            focusedIndex += 1;

            if (focusedIndex >= imagePaths.Count)
            {
                focusedIndex = 0; // wrap
            }
            updateUI();
        }

        private void OnPrevClicked(object sender, EventArgs e)
        {
            focusedIndex -= 1;
            if (focusedIndex < 0)
            {
                focusedIndex = imagePaths.Count - 1;
            }

            updateUI();
        }

        // Switch Collection / Content View
        int currentMode = 0;
        private void BiggerGroupClicked(Object sender, EventArgs e)
        {
            showCurrDay();
            MultiView.IsVisible = true;
            SingleView.IsVisible = false;

            currentMode = 1;
        }

        private void SmallerGroupClicked(Object sender, EventArgs e)
        {
            MultiView.IsVisible = false;
            SingleView.IsVisible = true;

            currentMode = 0;
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              MAP
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        private void updateUI()
        {
            ImagePath = imagePaths[focusedIndex];

            ImageMetadata metadata = imageDataCache.ContainsKey(imagePaths[focusedIndex]) ? imageDataCache[imagePaths[focusedIndex]] : readInfo(imagePaths[focusedIndex]);

            Info.Text = metadata.GetDisplayDate();

            if (metadata.Location != null)
            {
                string lat = metadata.Location.Latitude.ToString().Replace(",", ".");
                string lon = metadata.Location.Longitude.ToString().Replace(",", ".");

                Debug.WriteLine($"Updating Map Location to: {lat}, {lon}");
                webView.EvaluateJavaScriptAsync($"updateMapLocation({lat}, {lon});");
            }

            resetImage();
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              Zoom In/Out
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        //// Zoom Buttons
        //private void OnZoomInClicked(Object sender, EventArgs e)
        //{
        //    ZoomAtPoint(currentMode == 0 ? SingleView : MultiView, 360, 0, 0);
        //}
        //private void OnZoomOutClicked(Object sender, EventArgs e)
        //{
        //    ZoomAtPoint(currentMode == 0 ? SingleView : MultiView, -360, 0, 0);
        //}

        //// Mouse Wheel Zoom
        //bool insideImageArea = false;
        //private void OnPointerEntered(object sender, PointerEventArgs e)
        //{
        //    insideImageArea = true;
        //}
        //private void OnPointerExited(object sender, PointerEventArgs e)
        //{
        //    insideImageArea = false;
        //}
        //private async Task UiHook()
        //{
        //    UioHookProvider.Instance.KeyTypedEnabled = false;
        //    var hook = new EventLoopGlobalHook();

        //    hook.MouseWheel += OnMouseWheel;
        //    await hook.RunAsync();
        //}

        //private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
        //{
        //    MainThread.BeginInvokeOnMainThread(() =>
        //    {
        //        if (!insideImageArea)
        //        {
        //            return;
        //        }

        //        var viewOriginOnScreen = GetAbsolutePosition(currentMode == 0 ? SingleView : MultiView);
        //        var x = e.Data.X - viewOriginOnScreen.X;
        //        var y = e.Data.Y - viewOriginOnScreen.Y;

        //        Debug.WriteLine($"At {e.Data.X}, {e.Data.Y} zoomed for: {e.Data.Rotation}");

        //        ZoomAtPoint(currentMode == 0 ? SingleView : MultiView, e.Data.Rotation, x, y);
        //    });
        //}

        //// Calculate absolute position of a VisualElement (ContentView)
        //private static Point GetAbsolutePosition(VisualElement element)
        //{
        //    double x = 0, y = 0;
        //    while (element != null)
        //    {
        //        x += element.X;
        //        y += element.Y;
        //        if (element.Parent is VisualElement parent)
        //        {
        //            element = parent;
        //        }
        //        else
        //        {
        //            break;
        //        }
        //    }
        //    return new Point(x, y);
        //}

        //private async void ZoomAtPoint(VisualElement view, double wheelDelta, double cx, double cy)
        //{
        //    double _minScale = 0.25;
        //    double _maxScale = 10.0;
        //    var oldScale = view.Scale;

        //    // Wheel normalization (important)
        //    var zoomFactor = 1 + (wheelDelta / 1200.0); // tune 1200..2000
        //    var newScale = Math.Clamp(oldScale * zoomFactor, _minScale, _maxScale);

        //    if (Math.Abs(newScale - oldScale) < 0.0001)
        //        return;

        //    // Current translation
        //    var tx = view.TranslationX;
        //    var ty = view.TranslationY;

        //    // Core math
        //    var newTx = cx - (newScale / oldScale) * (cx - tx);
        //    var newTy = cy - (newScale / oldScale) * (cy - ty);

        //    //view.Scale = newScale;
        //    //view.TranslationX = newTx;
        //    //view.TranslationY = newTy;

        //    await view.TranslateToAsync(newTx, newTy, 60, Easing.CubicOut);
        //    await view.ScaleToAsync(newScale, 60, Easing.CubicOut);
        //}



        // Pan Handling
        double panX, panY;
        private void OnPanUpdated(Object sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // Capture current translation (may have been set by zoom)
                    panX = SingleView.TranslationX;
                    panY = SingleView.TranslationY;
                    break;

                case GestureStatus.Running:
                    // Translate and pan.
                    double boundsX = SingleView.Width;
                    double boundsY = SingleView.Height;
                    SingleView.TranslationX = Math.Clamp(panX + e.TotalX, -boundsX, boundsX);
                    SingleView.TranslationY = Math.Clamp(panY + e.TotalY, -boundsY, boundsY);
                    break;

                case GestureStatus.Completed:
                    // Store the translation applied during the pan
                    panX = SingleView.TranslationX;
                    panY = SingleView.TranslationY;
                    break;
            }
        }

        private void resetImage()
        {
            panX = 0;
            panY = 0;
            SingleView.TranslationX = 0;
            SingleView.TranslationY = 0;
            SingleView.ScaleToAsync(1, 250, Easing.CubicInOut);
        }

        
    }
}