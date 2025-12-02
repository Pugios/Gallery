using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Maui.Alerts;

namespace app
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        async void OnSelectFolderBtnClicked(object? sender, EventArgs e)
        {
            var (success, path) = await PickFolder(CancellationToken.None);

            if (success && path is not null)
            {
                string encoded = Uri.EscapeDataString(path);

                var paramdict = new Dictionary<string, object>()
                {
                    {"folderPath", encoded}
                };

                await Shell.Current.GoToAsync("ShowcasePage", paramdict);
            }
        }

        async Task<(bool IsSuccessful, string? FolderPath)> PickFolder(CancellationToken cancellationToken)
        {
            var result = await FolderPicker.Default.PickAsync(cancellationToken);
            if (result.IsSuccessful)
            {
                //await Toast.Make($"The folder was picked: Name - {result.Folder.Name}, Path - {result.Folder.Path}", ToastDuration.Long).Show(cancellationToken);
                return (true, result.Folder.Path);
            }
            else
            {
                await Toast.Make($"The folder was not picked with error: {result.Exception.Message}").Show(cancellationToken);
                return (false, null);
            }
        }
    }
}