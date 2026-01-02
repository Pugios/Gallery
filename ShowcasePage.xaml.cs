using app.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace app
{
    // Extending ContentPage (because its a page), and IQueryAttributable (to make it accept data as user is redirected here)
    public partial class ShowcasePage : ContentPage, IQueryAttributable
    {
        public ShowcasePage()
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
            updateUI(imageDataCache[imagePaths.First()]);
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

        public ObservableCollection<string> imagePaths { get; } = new();    // Collection of image paths for CarouselView
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
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Progress = progress;
                    });
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
        //                   UI
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%  

        // Button Controlls
        private void OnNextClicked(object sender, EventArgs e)
        {
            bool animate = true;
            var newIndex = carouselView.Position + 1;
            if (newIndex >= imagePaths.Count)
            {
                newIndex = 0; // wrap
                animate = false;
            }

            carouselView.ScrollTo(newIndex, animate: animate);
        }

        private void OnPrevClicked(object sender, EventArgs e)
        {
            bool animate = true;
            var newIndex = carouselView.Position - 1;
            if (newIndex < 0)
            {
                newIndex = imagePaths.Count - 1;
                animate = false;
            }
            carouselView.ScrollTo(newIndex, animate: animate);
        }

        private void updateUI(ImageMetadata metadata)
        {
            Info.Text = metadata.GetDisplayDate();

            if (metadata.Location != null){
                string lat = metadata.Location.Latitude.ToString().Replace(",", ".");
                string lon = metadata.Location.Longitude.ToString().Replace(",", ".");

                Debug.WriteLine($"Updating Map Location to: {lat}, {lon}");
                webView.EvaluateJavaScriptAsync($"updateMapLocation({lat}, {lon});");
            }
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              MAP
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        // When changing Picture in Carousel
        private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            var pos = e.CurrentPosition;
            if (pos >= 0 && pos < imagePaths.Count)
            {
                readInfo(imagePaths[pos]);
                updateUI(imageDataCache[imagePaths[pos]]);
            }
            //Debug.WriteLine($"Current Position: {pos}");
        }


    }
}