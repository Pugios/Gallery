using app.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;


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
        private void showFirstImg()
        {
            if (imagePaths?.Count > 0)
            {
                ImagePath = imagePaths[0];
            }
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


        private int viewMode = 0; // 0 = Single Image View, 1 Day, 2 Week, 3 Month

        private Dictionary<string, ImageMetadata> imageDataCache = new();   // Metadata for images
        public ObservableCollection<string> imagePaths { get; } = new();        // Single View Ordered Image Paths
        
        private readonly Dictionary<DateTime, List<string>> dayGroups = new();  // day-based grouping
        private readonly Dictionary<(int year, int week), List<string>> weekGroups = new(); // week-based grouping
        private readonly Dictionary<(int year, int month), List<string>> monthGroups = new(); // month-based grouping

        private List<DateTime> dayOrder = new(); // Ordered Days in dayGroups
        private List<(int year, int week)> weekOrder = new(); // Ordered Weeks in weekGroups
        private List<(int year, int month)> monthOrder = new(); // Ordered Months in monthGroups
        
        // Index of currently selected Group/Image 
        private int currImgIndex = 0;
        private int currDayIndex = 0;
        private int currWeekIndex = 0;
        private int currMonthIndex = 0;

        private async Task LoadImages()
        {
            imageDataCache.Clear();
            imagePaths.Clear();
            
            dayGroups.Clear();
            weekGroups.Clear();
            monthGroups.Clear();

            dayOrder.Clear();
            weekOrder.Clear();
            monthOrder.Clear();

            // Get all images in folder
            var files = System.IO.Directory.GetFiles(folderPath)
                             .Where(f =>
                                 f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)// ||
                                                                                       //f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                                                       //f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                 )
                             .ToList();


            // Read Metadata of Images
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

            // Add images to imagePaths sorted by DateTime
            foreach (string imagePath in imageDataCache.OrderBy(x => x.Value.DateTime).Select(x => x.Key))
            {
                imagePaths.Add(imagePath);
            }

            BuildGroupingIndexes();

            return;
        }

        Calendar calendar = System.Globalization.CultureInfo.InvariantCulture.Calendar; // for week calculations

        private void BuildGroupingIndexes()
        {
            if (imagePaths.Count == 0)
                return;
            

            // Go through each image and assign to groups
            foreach (var path in imagePaths)
            {
                var date = imageDataCache[path].DateTime?.Date ?? DateTime.MinValue.Date;

                // Day Groups
                // if date not already in dict add it
                if (!dayGroups.TryGetValue(date, out var listByDay))
                {
                    listByDay = new List<string>();
                    dayGroups[date] = listByDay;
                }
                // add the image to the day's list
                listByDay.Add(path);

                // Week Groups
                var week = calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                var weekKey = (date.Year, week);
                if (!weekGroups.TryGetValue(weekKey, out var listByWeek))
                {
                    listByWeek = new List<string>();
                    weekGroups[weekKey] = listByWeek;
                }
                listByWeek.Add(path);

                // Month Groups
                var month = date.Month;
                var monthKey = (date.Year, month);
                if (!monthGroups.TryGetValue(monthKey, out var listByMonth))
                {
                    listByMonth = new List<string>();
                    monthGroups[monthKey] = listByMonth;
                }
                listByMonth.Add(path);
            }

            dayOrder = dayGroups.Keys.OrderBy(d => d).ToList();
            weekOrder = weekGroups.Keys.OrderBy(d => d).ToList();
            monthOrder = monthGroups.Keys.OrderBy(d => d).ToList();
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
        //          Binding Views
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%% 

        // Single View
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

        // Group View
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

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //         Navigation
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%% 

        private void UpdateGridLayout(int itemCount)
        {
            GIL.Span = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(itemCount)));
        }

        private void BiggerGroupClicked(Object sender, EventArgs e)
        {
            switch(viewMode)
            {
                case 0:
                    // Show Day
                    DateTime currDay = imageDataCache[ImagePath].DateTime.GetValueOrDefault().Date;
                    currDayIndex = dayOrder.IndexOf(currDay);

                    GroupImagePaths = dayGroups[dayOrder[currDayIndex]];

                    viewMode = 1;

                    MultiView.IsVisible = true;
                    SingleView.IsVisible = false;
                    break;
                case 1:
                    // Show Week
                    DateTime currWeek = imageDataCache[GroupImagePaths[0]].DateTime.GetValueOrDefault().Date;
                    var week = calendar.GetWeekOfYear(currWeek, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                    currWeekIndex = weekOrder.IndexOf((currWeek.Year, week));

                    GroupImagePaths = weekGroups[weekOrder[currWeekIndex]];

                    viewMode = 2;
                    break;
                case 2:
                    // Show Month
                    DateTime currMonth = imageDataCache[GroupImagePaths[0]].DateTime.GetValueOrDefault().Date;
                    currMonthIndex = monthOrder.IndexOf((currMonth.Year, currMonth.Month));

                    GroupImagePaths = monthGroups[monthOrder[currMonthIndex]];

                    viewMode = 3;
                    break;
            }
            updateUI();
        }

        private void SmallerGroupClicked(Object sender, EventArgs e)
        {
            switch (viewMode)
            {
                case 1:
                    // Day -> Single
                    ImagePath = GroupImagePaths[0];
                    currImgIndex = imagePaths.IndexOf(ImagePath);

                    viewMode = 0;

                    MultiView.IsVisible = false;
                    SingleView.IsVisible = true;
                    break;
                case 2:
                    // Week -> Day
                    string firstImgPathInWeek = weekGroups[weekOrder[currWeekIndex]][0];
                    DateTime currDay = imageDataCache[firstImgPathInWeek].DateTime.GetValueOrDefault().Date;
                    currDayIndex = dayOrder.IndexOf(currDay);

                    GroupImagePaths = dayGroups[dayOrder[currDayIndex]];

                    viewMode = 1;
                    break;
                case 3:
                    // Month -> Week
                    string firstImgPathInMonth = monthGroups[monthOrder[currMonthIndex]][0];
                    DateTime currWeek = imageDataCache[firstImgPathInMonth].DateTime.GetValueOrDefault().Date;
                    var week = calendar.GetWeekOfYear(currWeek, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                    currWeekIndex = weekOrder.IndexOf((currWeek.Year, week));

                    GroupImagePaths = weekGroups[weekOrder[currWeekIndex]];
                    viewMode = 2;
                    break;
            }
            updateUI();
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            switch (viewMode)
            {
                case 0:
                    currImgIndex++;
                    if (currImgIndex >= imagePaths.Count)
                        currImgIndex = 0;
                    ImagePath = imagePaths[currImgIndex];
                    break;
                case 1:
                    currDayIndex++;
                    if (currDayIndex >= dayOrder.Count)
                        currDayIndex = 0;
                    GroupImagePaths = dayGroups[dayOrder[currDayIndex]];
                    break;
                case 2:
                    currWeekIndex++;
                    if (currWeekIndex >= weekOrder.Count)
                        currWeekIndex = 0;
                    GroupImagePaths = weekGroups[weekOrder[currWeekIndex]];
                    break;
                case 3:
                    currMonthIndex++;
                    if (currMonthIndex >= monthOrder.Count)
                        currMonthIndex = 0;
                    GroupImagePaths = monthGroups[monthOrder[currMonthIndex]];
                    break;
            }
            updateUI();
        }

        private void OnPrevClicked(object sender, EventArgs e)
        {
            switch (viewMode)
            {
                case 0:
                    currImgIndex--;
                    if (currImgIndex < 0)
                        currImgIndex = imagePaths.Count - 1;
                    ImagePath = imagePaths[currImgIndex];
                    break;
                case 1:
                    currDayIndex--;
                    if (currDayIndex < 0)
                        currDayIndex = dayOrder.Count - 1;
                    GroupImagePaths = dayGroups[dayOrder[currDayIndex]];
                    break;
                case 2:
                    currWeekIndex--;
                    if (currWeekIndex < 0)
                        currWeekIndex = weekOrder.Count - 1;
                    GroupImagePaths = weekGroups[weekOrder[currWeekIndex]];
                    break;
                case 3:
                    currMonthIndex--;
                    if (currMonthIndex < 0)
                        currMonthIndex = monthOrder.Count - 1;
                    GroupImagePaths = monthGroups[monthOrder[currMonthIndex]];
                    break;
            }
            updateUI();
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              MAP
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        private void updateUI()
        {
            //Info.Text = metadata.GetDisplayDate();

            //if (metadata.Location != null)
            //{
            //    string lat = metadata.Location.Latitude.ToString().Replace(",", ".");
            //    string lon = metadata.Location.Longitude.ToString().Replace(",", ".");

            //    Debug.WriteLine($"Updating Map Location to: {lat}, {lon}");
            //    webView.EvaluateJavaScriptAsync($"updateMapLocation({lat}, {lon});");
            //}

            if (viewMode >= 1)
            {
                UpdateGridLayout(GroupImagePaths.Count);
            }

            resetImage();
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              Zoom In/Out
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        //Zoom Buttons
        private void OnZoomInClicked(Object sender, EventArgs e)
        {
            SingleView.HandleWheel(360, 0.5, 0.5);
        }
        private void OnZoomOutClicked(Object sender, EventArgs e)
        {
            SingleView.HandleWheel(-360, 0.5, 0.5);
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //              Panning
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

        double panX, panY;
        private void OnPanUpdated(Object sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panX = SingleView.TranslationX;
                    panY = SingleView.TranslationY;
                    break;

                case GestureStatus.Running:
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

            this.InvalidateMeasure();
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