using Gallery2.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace Gallery2.Views.Pages;

/// <summary>
/// Interaction logic for GalleryPage.xaml
/// </summary>
public partial class GalleryPage : INavigableView<GalleryViewModel>
{
    public GalleryViewModel ViewModel { get; }
    public GalleryPage(GalleryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}