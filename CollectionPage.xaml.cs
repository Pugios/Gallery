using app.Models;
using app.Services;
using MetadataExtractor;
using System.Collections.ObjectModel;
using MetadataExtractor.Formats.Exif;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;


namespace app
{
    public partial class CollectionPage : ContentPage, IQueryAttributable
    {

        private static readonly string LikesFilePath =
            Path.Combine(FileSystem.AppDataDirectory, "likes.json");

        public CollectionPage()
        {
            InitializeComponent();
            BindingContext = this;
            SingleView.ZoomedOutBeyondMin = () => BiggerGroupClicked(null, EventArgs.Empty);
            LoadLikes();
        }

        private void LoadLikes()
        {
            try
            {
                if (File.Exists(LikesFilePath))
                {
                    var json = File.ReadAllText(LikesFilePath);
                    var paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                    if (paths != null)
                        foreach (var path in paths)
                            _likedPaths.Add(path);
                }
            }
            catch { }
        }

        private void SaveLikes()
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_likedPaths.ToList());
                File.WriteAllText(LikesFilePath, json);
            }
            catch { }
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
        
        private double _cellSize = 150;
        public double CellSize
        {
            get => _cellSize;
            set { _cellSize = value; OnPropertyChanged(nameof(CellSize)); }
        }

        private int _currentSpan = 3;

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width > 0 && height > 0 && viewMode >= 1)
                UpdateCellSize(width, height, GroupImagePaths.Count);
        }

        private void UpdateCellSize(double width, double height, int count)
        {
            if (width <= 0 || height <= 0 || count == 0) return;

            // Find the number of columns that best matches the screen aspect ratio
            int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(count * width / height)));
            int rows = (int)Math.Ceiling((double)count / cols);

            // Constrain cell size so all rows and columns fit on screen
            double cellSize = Math.Min(width / cols, height / rows);

            _currentSpan = cols;
            CellSize = cellSize;
        }

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
                    ThumbnailService.GenerateThumbnail(file);
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
            // Pretty sure this is wrong. Keys are (year, week)/(year, month) tuples, can't just order by d => d
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
                OnPropertyChanged(nameof(LikeButtonText));
            }
        }

        public string LikeButtonText => _likedPaths.Contains(_focusedImage) ? "♥" : "♡";

        // Group View
        private List<string> _focusedGroup = new();
        public List<string> GroupImagePaths
        {
            get => _focusedGroup;
            set
            {
                _focusedGroup = value ?? new List<string>();
                OnPropertyChanged(nameof(GroupImagePaths));
                _selectedPaths.Clear();
                _lastSelectedPath = null;
                RebuildGroupImageItems();
            }
        }

        public ObservableCollection<ImageItemViewModel> GroupImageItems { get; } = new();

        private void RebuildGroupImageItems()
        {
            GroupImageItems.Clear();
            foreach (var path in _focusedGroup)
                GroupImageItems.Add(new ImageItemViewModel(path) { IsLiked = _likedPaths.Contains(path) });
        }

        // Selection
        private readonly HashSet<string> _selectedPaths = new();
        private readonly HashSet<string> _likedPaths = new();
        private string? _lastSelectedPath = null;

        private void UpdateSelectionUI()
        {
            foreach (var item in GroupImageItems)
                item.IsSelected = _selectedPaths.Contains(item.Path);
        }

        private void SelectPath(string path)
        {
            _selectedPaths.Clear();
            _selectedPaths.Add(path);
            _lastSelectedPath = path;
            UpdateSelectionUI();
        }

        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
        //         Navigation
        // %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%% 

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
                    SingleView.Opacity = 0;
                    updateUI();
                    SelectPath(ImagePath);
                    return;
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

        private async Task GoToSingleView(string imagePath)
        {
            SingleView.Opacity = 0;
            currImgIndex = imagePaths.IndexOf(imagePath);
            ImagePath = imagePath;
            viewMode = 0;
            MultiView.IsVisible = false;
            SingleView.IsVisible = true;
            updateUI();
            await SingleView.FadeTo(1, 200, Easing.CubicIn);
        }

        private async void SmallerGroupClicked(Object sender, EventArgs e)
        {
            switch (viewMode)
            {
                case 1:
                    // Day -> Single
                    await GoToSingleView(GroupImagePaths[0]);
                    return;
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

        private void OnImageSingleTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not string imagePath) return;

            bool ctrl = KeyboardHelper.IsControlPressed();
            bool shift = KeyboardHelper.IsShiftPressed();

            if (ctrl)
            {
                if (!_selectedPaths.Remove(imagePath))
                    _selectedPaths.Add(imagePath);
                _lastSelectedPath = imagePath;
            }
            else if (shift && _lastSelectedPath != null)
            {
                int start = _focusedGroup.IndexOf(_lastSelectedPath);
                int end = _focusedGroup.IndexOf(imagePath);
                if (start > end) (start, end) = (end, start);
                for (int i = start; i <= end; i++)
                    _selectedPaths.Add(_focusedGroup[i]);
            }
            else
            {
                _selectedPaths.Clear();
                _selectedPaths.Add(imagePath);
                _lastSelectedPath = imagePath;
            }

            UpdateSelectionUI();
        }

        private void OnLikeClicked(object sender, EventArgs e)
        {
            if (viewMode == 0)
            {
                // Toggle like for current single image
                if (!_likedPaths.Remove(_focusedImage))
                    _likedPaths.Add(_focusedImage);
                OnPropertyChanged(nameof(LikeButtonText));
            }
            else
            {
                // If all selected are already liked, unlike them; otherwise like all
                bool allLiked = _selectedPaths.Count > 0 && _selectedPaths.All(p => _likedPaths.Contains(p));
                if (allLiked)
                    foreach (var path in _selectedPaths)
                        _likedPaths.Remove(path);
                else
                    foreach (var path in _selectedPaths)
                        _likedPaths.Add(path);

                foreach (var item in GroupImageItems)
                    item.IsLiked = _likedPaths.Contains(item.Path);
            }

            SaveLikes();
        }

        private async void OnImageDoubleTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not string imagePath) return;

            var date = imageDataCache[imagePath].DateTime.GetValueOrDefault().Date;

            switch (viewMode)
            {
                case 3:
                    // Month → Week
                    var week = calendar.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                    currWeekIndex = weekOrder.IndexOf((date.Year, week));
                    GroupImagePaths = weekGroups[weekOrder[currWeekIndex]];
                    viewMode = 2;
                    break;
                case 2:
                    // Week → Day
                    currDayIndex = dayOrder.IndexOf(date);
                    GroupImagePaths = dayGroups[dayOrder[currDayIndex]];
                    viewMode = 1;
                    break;
                case 1:
                    // Day → Single
                    await GoToSingleView(imagePath);
                    return;
            }

            updateUI();
            SelectPath(imagePath);
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
            // Update Information Text
            switch (viewMode)
            {
                case 0:
                    {
                        ImageMetadata metadata = imageDataCache[imagePaths[currImgIndex]];
                        Info.Text = metadata.GetDisplayDate();
                        break;
                    }
                case 1:
                    {
                        var group = dayGroups[dayOrder[currDayIndex]];
                        var begin = imageDataCache[group[0]].DateTime;
                        var end = imageDataCache[group[^1]].DateTime;
                        Info.Text = FormatDateRange(begin, end);
                        break;
                    }
                case 2:
                    {
                        var group = weekGroups[weekOrder[currWeekIndex]];
                        var begin = imageDataCache[group[0]].DateTime;
                        var end = imageDataCache[group[^1]].DateTime;
                        Info.Text = FormatDateRange(begin, end);
                        break;
                    }
                case 3:
                    {
                        var group = monthGroups[monthOrder[currMonthIndex]];
                        var begin = imageDataCache[group[0]].DateTime;
                        var end = imageDataCache[group[^1]].DateTime;
                        Info.Text = FormatDateRange(begin, end);
                        break;
                    }
            }
            // Update Map
            if (viewMode == 0)
                UpdateMap(new[] { imagePaths[currImgIndex] });
            else
                UpdateMap(GroupImagePaths);

            // Update Grid Layout
            if (viewMode >= 1)
                UpdateCellSize(Width, Height, GroupImagePaths.Count);

            resetImage();
        }

        private static string FormatDateRange(DateTime? begin, DateTime? end)
        {
            const string fmt = "d MMM yyyy";
            string beginStr = begin?.ToString(fmt, CultureInfo.InvariantCulture) ?? "?";
            if (end == null || end.Value.Date == begin?.Date)
                return beginStr;
            return $"{beginStr} – {end.Value.ToString(fmt, CultureInfo.InvariantCulture)}";
        }

        private bool _mapReady = false;
        private string? _pendingMapJs = null;

        private void OnMapNavigated(object sender, WebNavigatedEventArgs e)
        {
            _mapReady = true;
            if (_pendingMapJs != null)
            {
                webView.EvaluateJavaScriptAsync(_pendingMapJs);
                _pendingMapJs = null;
            }
        }

        private void UpdateMap(IEnumerable<string> paths)
        {
            var coords = paths
                .Select(p => imageDataCache.TryGetValue(p, out var m) ? m.Location : null)
                .Where(loc => loc != null)
                .Select(loc => $"{{\"lat\":{loc!.Latitude.ToString(CultureInfo.InvariantCulture)},\"lon\":{loc.Longitude.ToString(CultureInfo.InvariantCulture)}}}")
                .ToList();

            string js = $"setMarkers([{string.Join(",", coords)}]);";

            if (_mapReady)
                webView.EvaluateJavaScriptAsync(js);
            else
                _pendingMapJs = js;
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
                    var scale = SingleView.Scale;
                    double minTx = -(scale - 1) * SingleView.Width;
                    double minTy = -(scale - 1) * SingleView.Height;

                    SingleView.TranslationX = Math.Clamp(panX + e.TotalX, minTx, 0);
                    SingleView.TranslationY = Math.Clamp(panY + e.TotalY, minTy, 0);
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