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

        protected string folderPath { get; private set; }

        // Accept Query Data aka. Folder Path of Images
        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            folderPath = Uri.UnescapeDataString(query["folderPath"] as string);
            OnPropertyChanged("folderPath");
            PopulateCarousel();

            readInfo(ImagePaths.First());
        }

        // Show images in the Carousel
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

        // EXIF Data Reading and showing information!
        // Cache to avoid repeatedly reading EXIF for the same file
        readonly Dictionary<string, string> _dateCache = new();

        private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
        {
            var pos = e.CurrentPosition;
            if (pos >= 0 && pos < ImagePaths.Count)
                readInfo(ImagePaths[pos]);
            Debug.WriteLine($"Current Position: {pos}");
        }

        private void readInfo(string imagePath)
        {
            // Check if we already have the date cached
            if (_dateCache.TryGetValue(imagePath, out var cachedDate))
            {
                Info.Text = cachedDate;
                return;
            }

            IEnumerable<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(imagePath);

            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            string datetime = "No Date Found";
            if (exifSubIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal,out DateTime dateTimeOriginal)??false)
            {
                datetime = dateTimeOriginal.ToString();
            }
            Info.Text = datetime;
            _dateCache[imagePath] = datetime;
            Debug.WriteLine($"DateTime: {datetime}");
        }
    }
}