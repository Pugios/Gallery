namespace app
{
    public partial class ShowcasePage : ContentPage, IQueryAttributable
    {
        public ShowcasePage()
        {
            InitializeComponent();
        }

        protected string folderPath { get; private set; }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            folderPath = Uri.UnescapeDataString(query["folderPath"] as string);
            OnPropertyChanged("folderPath");

            FolderPathLabel.Text = folderPath;
        }

        //readonly string[] ImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
        //readonly string[] VideoExtensions = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };

        //ObservableCollection<MediaItem> Items { get; } = new();

        //string _folderPath = string.Empty;
        //public string FolderPath
        //{
        //    get => _folderPath;
        //    set
        //    {
        //        if (_folderPath == value)
        //            return;

        //        // Shell passes an encoded query string; decode it
        //        _folderPath = value is null ? string.Empty : Uri.UnescapeDataString(value);
        //        _ = LoadMediaAsync(_folderPath);
        //    }
        //}



        //async Task LoadMediaAsync(string path)
        //{
        //    try
        //    {
        //        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        //        {
        //            FolderPathLabel.Text = "Folder not found";
        //            return;
        //        }

        //        FolderPathLabel.Text = path;

        //        Items.Clear();

        //        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
        //                             .Where(f =>
        //                             {
        //                                 var ext = Path.GetExtension(f)?.ToLowerInvariant() ?? string.Empty;
        //                                 return ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);
        //                             })
        //                             .ToList();

        //        foreach (var f in files)
        //        {
        //            var ext = Path.GetExtension(f)?.ToLowerInvariant() ?? string.Empty;
        //            Items.Add(new MediaItem
        //            {
        //                FilePath = f,
        //                FileName = Path.GetFileName(f),
        //                IsImage = ImageExtensions.Contains(ext),
        //                IsVideo = VideoExtensions.Contains(ext)
        //            });
        //        }

        //        if (Items.Any())
        //            carousel.Position = 0;

        //        FolderPathLabel.Text = $"{path} ({Items.Count} media files)";
        //    }
        //    catch (Exception ex)
        //    {
        //        FolderPathLabel.Text = $"Error reading folder: {ex.Message}";
        //    }

        //    await Task.CompletedTask;
        //}

        //void OnGoLeftClicked(object sender, EventArgs e)
        //{
        //    if (Items.Count == 0) return;
        //    var pos = carousel.Position;
        //    if (pos > 0) carousel.Position = pos - 1;
        //}

        //void OnGoRightClicked(object sender, EventArgs e)
        //{
        //    if (Items.Count == 0) return;
        //    var pos = carousel.Position;
        //    if (pos < Items.Count - 1) carousel.Position = pos + 1;
        //}

        //async void OnPlayClicked(object sender, EventArgs e)
        //{
        //    if (sender is Button btn && btn.CommandParameter is string path && File.Exists(path))
        //    {
        //        try
        //        {
        //            var file = new ReadOnlyFile(path);
        //            var request = new OpenFileRequest
        //            {
        //                File = file,
        //                Title = Path.GetFileName(path)
        //            };
        //            await Launcher.Default.OpenAsync(request);
        //        }
        //        catch (Exception ex)
        //        {
        //            await DisplayAlert("Error", $"Unable to open file: {ex.Message}", "OK");
        //        }
        //    }
        //}
    }
}