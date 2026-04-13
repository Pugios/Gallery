using Gallery2.Models;
using Gallery2.Services;
using Gallery2.ViewModels.Pages;
using Gallery2.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace Gallery2.Views.Pages;

public partial class GalleryPage : INavigableView<GalleryViewModel>
{
    public GalleryViewModel ViewModel { get; }
    private ScrollViewer? _scrollViewer;

    public GalleryPage(GalleryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindScrollViewer(ImageGrid);
    }

    private void ImageGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListViewItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is ListViewItem { DataContext: PictureItem picture })
        {
            var window = App.Services.GetRequiredService<ImageWindow>();
            window.ViewModel.Load(picture);
            window.Show();
        }
    }

    private void ImageGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scrollViewer is null) return;
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - 45.0 * e.Delta / 120.0);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
