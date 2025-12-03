using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core.Primitives;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.Threading;

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

        // Accept Query Data
        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            folderPath = Uri.UnescapeDataString(query["folderPath"] as string);
            OnPropertyChanged("folderPath");
            PopulateCarousel();
        }

        public ObservableCollection<string> ImagePaths { get; } = new();
        private void PopulateCarousel()
        {
            ImagePaths.Clear();

            var files = Directory.GetFiles(folderPath)
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

    }
}