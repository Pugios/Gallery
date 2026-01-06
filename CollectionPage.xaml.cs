using app;
using app.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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
            ImagePath = imagePaths[focusedIndex];
        }

        private void OnPrevClicked(object sender, EventArgs e)
        {
            focusedIndex -= 1;
            if (focusedIndex < 0)
            {
                focusedIndex = imagePaths.Count - 1;
            }
            ImagePath = imagePaths[focusedIndex];
        }

        private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            var pos = e.CurrentPosition;
            if (pos >= 0 && pos < imagePaths.Count)
            {
                readInfo(imagePaths[pos]);
                updateUI(imageDataCache[imagePaths[pos]]);
            }
        }

        // Switch Collection / Content View
        private void OnZoomOutClicked(Object sender, EventArgs e)
        {
            showCurrDay();
            MultiView.IsVisible = true;

            SingleView.IsVisible = false;
        }


        private void OnZoomInClicked(Object sender, EventArgs e)
        {
            MultiView.IsVisible = false;

            SingleView.IsVisible = true;
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              MAP
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        private void updateUI(ImageMetadata metadata)
        {
            Info.Text = metadata.GetDisplayDate();

            if (metadata.Location != null)
            {
                string lat = metadata.Location.Latitude.ToString().Replace(",", ".");
                string lon = metadata.Location.Longitude.ToString().Replace(",", ".");

                Debug.WriteLine($"Updating Map Location to: {lat}, {lon}");
                webView.EvaluateJavaScriptAsync($"updateMapLocation({lat}, {lon});");
            }
        }
    }
}