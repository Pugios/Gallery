using app.Models;
using app.Services;
using System.Collections.ObjectModel;
using System.Globalization;

namespace app
{
    public partial class CollectionPage : ContentPage, IQueryAttributable
    {
        public CollectionPage()
        {
            InitializeComponent();
            BindingContext = this;
            SingleView.ZoomedOutBeyondMin = () => BiggerGroupClicked(null, EventArgs.Empty);
            _likedPaths = LikeService.Load();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (webView.Source == null)
            {
#if WINDOWS
                // WebView2 can't write its cache to Program Files.
                var userDataFolder = Path.Combine(FileSystem.AppDataDirectory, "WebView2");
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);
#endif
                using var stream = await FileSystem.OpenAppPackageFileAsync("map.html");
                using var reader = new StreamReader(stream);
                var html = await reader.ReadToEndAsync();
                webView.Source = new HtmlWebViewSource { Html = html };
            }
        }

        // ── Folder loading ────────────────────────────────────────────────

        protected string FolderPath { get; private set; } = string.Empty;

        public async void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            IsLoading = true;
            Progress = 0;

            FolderPath = Uri.UnescapeDataString(query["folderPath"] as string ?? string.Empty);

            var progressReporter = new Progress<double>(p =>
                MainThread.BeginInvokeOnMainThread(() => Progress = p));

            var data = await ImageLoadingService.LoadFolderAsync(FolderPath, progressReporter);

            _imageDataCache = data.ImageDataCache;
            foreach (var path in data.ImagePaths)
                ImagePaths.Add(path);

            _dayGroups   = data.DayGroups;
            _weekGroups  = data.WeekGroups;
            _monthGroups = data.MonthGroups;
            _dayOrder    = data.DayOrder;
            _weekOrder   = data.WeekOrder;
            _monthOrder  = data.MonthOrder;

            Progress = 1;
            IsLoading = false;

            if (ImagePaths.Count > 0)
                ImagePath = ImagePaths[0];

            UpdateUI();
        }

        // ── Loading progress ──────────────────────────────────────────────

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress == value) return;
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }

        // ── Data ──────────────────────────────────────────────────────────

        private Dictionary<string, ImageMetadata> _imageDataCache = new();
        public ObservableCollection<string> ImagePaths { get; } = new();

        private Dictionary<DateTime, List<string>> _dayGroups   = new();
        private Dictionary<(int year, int week), List<string>> _weekGroups  = new();
        private Dictionary<(int year, int month), List<string>> _monthGroups = new();

        private List<DateTime> _dayOrder   = new();
        private List<(int year, int week)> _weekOrder  = new();
        private List<(int year, int month)> _monthOrder = new();

        // ── View state ────────────────────────────────────────────────────

        private ViewMode _viewMode = ViewMode.Single;

        private int _currImgIndex   = 0;
        private int _currDayIndex   = 0;
        private int _currWeekIndex  = 0;
        private int _currMonthIndex = 0;

        // ── Cell sizing ───────────────────────────────────────────────────

        private int _currentSpan = 3;

        private double _cellSize = 150;
        public double CellSize
        {
            get => _cellSize;
            set { _cellSize = value; OnPropertyChanged(nameof(CellSize)); }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width > 0 && height > 0 && _viewMode != ViewMode.Single)
                UpdateCellSize(width, height, GroupImagePaths.Count);
        }

        private void UpdateCellSize(double width, double height, int count)
        {
            if (width <= 0 || height <= 0 || count == 0) return;
            int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(count * width / height)));
            int rows = (int)Math.Ceiling((double)count / cols);
            _currentSpan = cols;
            CellSize = Math.Min(width / cols, height / rows);
        }

        // ── Bound properties ──────────────────────────────────────────────

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

        private List<string> _focusedGroup = new();
        public List<string> GroupImagePaths
        {
            get => _focusedGroup;
            private set
            {
                _focusedGroup = value ?? new List<string>();
                OnPropertyChanged(nameof(GroupImagePaths));
                _selectedPaths.Clear();
                _lastSelectedPath = null;
            }
        }

        private List<ImageItemViewModel> _groupImageItems = new();
        public List<ImageItemViewModel> GroupImageItems
        {
            get => _groupImageItems;
            private set { _groupImageItems = value; OnPropertyChanged(nameof(GroupImageItems)); }
        }

        private async Task GoToGroupAsync(List<string> paths)
        {
            GroupImagePaths = paths;
            UpdateUI();

            var liked = _likedPaths;
            GroupImageItems = await Task.Run(() =>
                paths.Select(p => new ImageItemViewModel(p) { IsLiked = liked.Contains(p) }).ToList());
        }

        // ── Selection ─────────────────────────────────────────────────────

        private readonly HashSet<string> _selectedPaths = new();
        private string? _lastSelectedPath;

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

        // ── Likes ─────────────────────────────────────────────────────────

        private readonly HashSet<string> _likedPaths = new();

        private void OnLikeClicked(object sender, EventArgs e)
        {
            if (_viewMode == ViewMode.Single)
            {
                if (!_likedPaths.Remove(_focusedImage))
                    _likedPaths.Add(_focusedImage);
                OnPropertyChanged(nameof(LikeButtonText));
            }
            else
            {
                bool allLiked = _selectedPaths.Count > 0 && _selectedPaths.All(p => _likedPaths.Contains(p));
                if (allLiked)
                    foreach (var path in _selectedPaths) _likedPaths.Remove(path);
                else
                    foreach (var path in _selectedPaths) _likedPaths.Add(path);

                foreach (var item in GroupImageItems)
                    item.IsLiked = _likedPaths.Contains(item.Path);
            }

            LikeService.Save(_likedPaths);
        }

        // ── Navigation ────────────────────────────────────────────────────

        private async void BiggerGroupClicked(object? sender, EventArgs e)
        {
            switch (_viewMode)
            {
                case ViewMode.Single:
                    var day = _imageDataCache[ImagePath].DateTime.GetValueOrDefault().Date;
                    _currDayIndex = _dayOrder.IndexOf(day);
                    _viewMode = ViewMode.Day;
                    MultiView.IsVisible = true;
                    SingleView.IsVisible = false;
                    SingleView.Opacity = 0;
                    var selectedPath = ImagePath;
                    await GoToGroupAsync(_dayGroups[_dayOrder[_currDayIndex]]);
                    SelectPath(selectedPath);
                    return;

                case ViewMode.Day:
                    var weekDate = _imageDataCache[GroupImagePaths[0]].DateTime.GetValueOrDefault().Date;
                    _currWeekIndex = _weekOrder.IndexOf((weekDate.Year, ImageLoadingService.GetWeekNumber(weekDate)));
                    _viewMode = ViewMode.Week;
                    await GoToGroupAsync(_weekGroups[_weekOrder[_currWeekIndex]]);
                    return;

                case ViewMode.Week:
                    var monthDate = _imageDataCache[GroupImagePaths[0]].DateTime.GetValueOrDefault().Date;
                    _currMonthIndex = _monthOrder.IndexOf((monthDate.Year, monthDate.Month));
                    _viewMode = ViewMode.Month;
                    await GoToGroupAsync(_monthGroups[_monthOrder[_currMonthIndex]]);
                    return;
            }
        }

        private async Task GoToSingleView(string imagePath)
        {
            SingleView.Opacity = 0;
            _currImgIndex = ImagePaths.IndexOf(imagePath);
            ImagePath = imagePath;
            _viewMode = ViewMode.Single;
            MultiView.IsVisible = false;
            SingleView.IsVisible = true;
            UpdateUI();
            await SingleView.FadeToAsync(1, 200, Easing.CubicIn);
        }

        private async void SmallerGroupClicked(object? sender, EventArgs e)
        {
            switch (_viewMode)
            {
                case ViewMode.Day:
                    await GoToSingleView(GroupImagePaths[0]);
                    return;

                case ViewMode.Week:
                    var dayDate = _imageDataCache[_weekGroups[_weekOrder[_currWeekIndex]][0]].DateTime.GetValueOrDefault().Date;
                    _currDayIndex = _dayOrder.IndexOf(dayDate);
                    _viewMode = ViewMode.Day;
                    await GoToGroupAsync(_dayGroups[_dayOrder[_currDayIndex]]);
                    return;

                case ViewMode.Month:
                    var weekDate = _imageDataCache[_monthGroups[_monthOrder[_currMonthIndex]][0]].DateTime.GetValueOrDefault().Date;
                    _currWeekIndex = _weekOrder.IndexOf((weekDate.Year, ImageLoadingService.GetWeekNumber(weekDate)));
                    _viewMode = ViewMode.Week;
                    await GoToGroupAsync(_weekGroups[_weekOrder[_currWeekIndex]]);
                    return;
            }
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            switch (_viewMode)
            {
                case ViewMode.Single:
                    _currImgIndex = (_currImgIndex + 1) % ImagePaths.Count;
                    ImagePath = ImagePaths[_currImgIndex];
                    UpdateUI();
                    return;
                case ViewMode.Day:
                    _currDayIndex = (_currDayIndex + 1) % _dayOrder.Count;
                    await GoToGroupAsync(_dayGroups[_dayOrder[_currDayIndex]]);
                    return;
                case ViewMode.Week:
                    _currWeekIndex = (_currWeekIndex + 1) % _weekOrder.Count;
                    await GoToGroupAsync(_weekGroups[_weekOrder[_currWeekIndex]]);
                    return;
                case ViewMode.Month:
                    _currMonthIndex = (_currMonthIndex + 1) % _monthOrder.Count;
                    await GoToGroupAsync(_monthGroups[_monthOrder[_currMonthIndex]]);
                    return;
            }
        }

        private async void OnPrevClicked(object sender, EventArgs e)
        {
            switch (_viewMode)
            {
                case ViewMode.Single:
                    _currImgIndex = (_currImgIndex - 1 + ImagePaths.Count) % ImagePaths.Count;
                    ImagePath = ImagePaths[_currImgIndex];
                    UpdateUI();
                    return;
                case ViewMode.Day:
                    _currDayIndex = (_currDayIndex - 1 + _dayOrder.Count) % _dayOrder.Count;
                    await GoToGroupAsync(_dayGroups[_dayOrder[_currDayIndex]]);
                    return;
                case ViewMode.Week:
                    _currWeekIndex = (_currWeekIndex - 1 + _weekOrder.Count) % _weekOrder.Count;
                    await GoToGroupAsync(_weekGroups[_weekOrder[_currWeekIndex]]);
                    return;
                case ViewMode.Month:
                    _currMonthIndex = (_currMonthIndex - 1 + _monthOrder.Count) % _monthOrder.Count;
                    await GoToGroupAsync(_monthGroups[_monthOrder[_currMonthIndex]]);
                    return;
            }
        }

        // ── Tap handling (single handler to avoid double-tap delay) ──────

        private string? _lastTappedPath;
        private DateTime _lastTapTime = DateTime.MinValue;
        private const int DoubleTapMs = 300;

        private async void OnImageTapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not string imagePath) return;

            var now = DateTime.UtcNow;
            bool isDouble = imagePath == _lastTappedPath &&
                            (now - _lastTapTime).TotalMilliseconds <= DoubleTapMs;

            _lastTappedPath = imagePath;
            _lastTapTime    = now;

            if (isDouble)
            {
                await HandleDoubleTap(imagePath);
            }
            else
            {
                HandleSingleTap(imagePath);
            }
        }

        private async Task HandleDoubleTap(string imagePath)
        {
            var date = _imageDataCache[imagePath].DateTime.GetValueOrDefault().Date;

            switch (_viewMode)
            {
                case ViewMode.Month:
                    _currWeekIndex = _weekOrder.IndexOf((date.Year, ImageLoadingService.GetWeekNumber(date)));
                    _viewMode = ViewMode.Week;
                    await GoToGroupAsync(_weekGroups[_weekOrder[_currWeekIndex]]);
                    SelectPath(imagePath);
                    return;
                case ViewMode.Week:
                    _currDayIndex = _dayOrder.IndexOf(date);
                    _viewMode = ViewMode.Day;
                    await GoToGroupAsync(_dayGroups[_dayOrder[_currDayIndex]]);
                    SelectPath(imagePath);
                    return;
                case ViewMode.Day:
                    await GoToSingleView(imagePath);
                    return;
            }
        }

        private void HandleSingleTap(string imagePath)
        {
            bool ctrl  = KeyboardHelper.IsControlPressed();
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
                int end   = _focusedGroup.IndexOf(imagePath);
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

        // ── UI update ─────────────────────────────────────────────────────

        private void UpdateUI()
        {
            switch (_viewMode)
            {
                case ViewMode.Single:
                    Info.Text = _imageDataCache[ImagePaths[_currImgIndex]].GetDisplayDate();
                    break;
                case ViewMode.Day:
                    var dg = _dayGroups[_dayOrder[_currDayIndex]];
                    Info.Text = FormatDateRange(_imageDataCache[dg[0]].DateTime, _imageDataCache[dg[^1]].DateTime);
                    break;
                case ViewMode.Week:
                    var wg = _weekGroups[_weekOrder[_currWeekIndex]];
                    Info.Text = FormatDateRange(_imageDataCache[wg[0]].DateTime, _imageDataCache[wg[^1]].DateTime);
                    break;
                case ViewMode.Month:
                    var mg = _monthGroups[_monthOrder[_currMonthIndex]];
                    Info.Text = FormatDateRange(_imageDataCache[mg[0]].DateTime, _imageDataCache[mg[^1]].DateTime);
                    break;
            }

            if (_viewMode == ViewMode.Single)
                UpdateMap(new[] { ImagePaths[_currImgIndex] });
            else
                UpdateMap(GroupImagePaths);

            if (_viewMode != ViewMode.Single)
                UpdateCellSize(Width, Height, GroupImagePaths.Count);

            ResetImage();
        }

        private static string FormatDateRange(DateTime? begin, DateTime? end)
        {
            const string fmt = "d MMM yyyy";
            string beginStr = begin?.ToString(fmt, CultureInfo.InvariantCulture) ?? "?";
            if (end == null || end.Value.Date == begin?.Date)
                return beginStr;
            return $"{beginStr} – {end.Value.ToString(fmt, CultureInfo.InvariantCulture)}";
        }

        // ── Map ───────────────────────────────────────────────────────────

        private bool _mapReady;
        private string? _pendingMapJs;

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
                .Select(p => _imageDataCache.TryGetValue(p, out var m) ? m.Location : null)
                .Where(loc => loc != null)
                .Select(loc => $"{{\"lat\":{loc!.Latitude.ToString(CultureInfo.InvariantCulture)},\"lon\":{loc.Longitude.ToString(CultureInfo.InvariantCulture)}}}")
                .ToList();

            string js = $"setMarkers([{string.Join(",", coords)}]);";

            if (_mapReady)
                webView.EvaluateJavaScriptAsync(js);
            else
                _pendingMapJs = js;
        }

        // ── Pan & zoom ────────────────────────────────────────────────────

        private double _panX, _panY;

        private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panX = SingleView.TranslationX;
                    _panY = SingleView.TranslationY;
                    break;

                case GestureStatus.Running:
                    var scale = SingleView.Scale;
                    SingleView.TranslationX = Math.Clamp(_panX + e.TotalX, -(scale - 1) * SingleView.Width,  0);
                    SingleView.TranslationY = Math.Clamp(_panY + e.TotalY, -(scale - 1) * SingleView.Height, 0);
                    break;

                case GestureStatus.Completed:
                    _panX = SingleView.TranslationX;
                    _panY = SingleView.TranslationY;
                    break;
            }

            InvalidateMeasure();
        }

        private void OnSingleViewDoubleTapped(object sender, TappedEventArgs e) => ResetImage();

        private void ResetImage()
        {
            _panX = 0;
            _panY = 0;
            SingleView.TranslationX = 0;
            SingleView.TranslationY = 0;
            SingleView.ScaleToAsync(1, 250, Easing.CubicInOut);
        }
    }
}
