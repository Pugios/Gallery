using app.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Collections.ObjectModel;
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

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            folderPath = Uri.UnescapeDataString(query["folderPath"] as string);
            OnPropertyChanged("folderPath");
            PopulateCarousel();

            readInfo(ImagePaths.First());
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //                  Carousel
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        public ObservableCollection<string> ImagePaths { get; } = new();
        private void PopulateCarousel()
        {
            ImagePaths.Clear();

            var files = System.IO.Directory.GetFiles(folderPath)
                             .Where(f =>
                                 f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)// ||
                                                                                       //f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                                                       //f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                 )
                             .ToList();

            // Add files to a collection bound to your CarouselView
            foreach (var file in files)
            {
                ImagePaths.Add(file);
            }
        }

        // Button Controlls
        private void OnNextClicked(object sender, EventArgs e)
        {
            bool animate = true;
            var newIndex = carouselView.Position + 1;
            if (newIndex >= ImagePaths.Count)
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
                newIndex = ImagePaths.Count - 1;
                animate = false;
            }

            carouselView.ScrollTo(newIndex, animate: animate);
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              EXIF Data Reading
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        readonly Dictionary<string, ImageMetadata> imageDataCache = new();
        private void readInfo(string imagePath)
        {
            // Check if we already have the date cached
            if (imageDataCache.TryGetValue(imagePath, out var cachedMetadata))
            {
                updateUI(cachedMetadata);
                return;
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

            if (gpsDirectory?.TryGetGeoLocation(out var geoLocation) ?? false){
                metadata.Location = new GpsCoordinates()
                {
                    Latitude = geoLocation.Latitude,
                    Longitude = geoLocation.Longitude
                };
            }

            // Image Direction
            if(gpsDirectory?.TryGetDouble(GpsDirectory.TagImgDirection, out double direction) ?? false)
            {
                metadata.ImageDirection = direction;

                // Magnetic Direction
                var directionRef = gpsDirectory.GetString(GpsDirectory.TagImgDirectionRef);
                metadata.IsMagneticDirection = directionRef == "M";
            }

            imageDataCache.Add(imagePath, metadata);
            updateUI(metadata);

            /*
            Debug.WriteLine($"Data for {imagePath}");
            foreach (var directory in directories)
                foreach (var tag in directory.Tags)
                {
                    Debug.WriteLine($"Directory {directory.Name} - Tag {tag.Name} = {tag.Description}");
                }
            */
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

        // When changing Picture in Carousel
        private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            var pos = e.CurrentPosition;
            if (pos >= 0 && pos < ImagePaths.Count)
                readInfo(ImagePaths[pos]);
            //Debug.WriteLine($"Current Position: {pos}");
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              MAP
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    }
}