using Gallery2.Models;
using Gallery2.Services;
using Gallery2.ViewModels.Pages;
using Gallery2.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;
using Size = System.Windows.Size;

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
        //Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindScrollViewer(ImageGrid);
        ApplyThumbnailSize(ViewModel.GalleryState.ImageSizePixels);
        ViewModel.GalleryState.PropertyChanged += OnGalleryStatePropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.GalleryState.PropertyChanged -= OnGalleryStatePropertyChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // Change Thumbnail Size
    // ======================================

    // If Done Loading
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.IsLoading) && !ViewModel.IsLoading)
            Dispatcher.InvokeAsync(() => ApplyThumbnailSize(ViewModel.GalleryState.ImageSizePixels),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // If Image Size Changed in Toolbar
    private void OnGalleryStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryState.ImageSizePixels))
            ApplyThumbnailSize(ViewModel.GalleryState.ImageSizePixels);
    }

    private void ApplyThumbnailSize(int pixels)
    {
        var panel = FindVisualChild<Wpf.Ui.Controls.VirtualizingWrapPanel>(ImageGrid);
        if (panel is null) return;
        panel.ItemSize = new Size(pixels, pixels);
        ImageGrid.InvalidateMeasure();
        ImageGrid.UpdateLayout();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    // Double Click - Open image
    // ======================================
    private void ImageGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListViewItem)
            dep = VisualTreeHelper.GetParent(dep);

        // Open in new Custom Window

        //if (dep is ListViewItem { DataContext: PictureItem picture })
        //{
        //    var window = App.Services.GetRequiredService<ImageWindow>();
        //    window.ViewModel.Load(picture);
        //    window.Show();
        //}

        // Open with default image viewer

        if (dep is ListViewItem { DataContext: PictureItem picture })
        {
            Process.Start(new ProcessStartInfo(picture.FilePath) { UseShellExecute = true });
        }

    }

    // Scroll Speed
    // ======================================
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
