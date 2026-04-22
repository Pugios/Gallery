using Gallery2.Models;
using Gallery2.ViewModels.Pages;
using Gallery2.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace Gallery2.Views.Pages;

public partial class SearchPage : INavigableView<SearchViewModel>
{
    public SearchViewModel ViewModel { get; }
    private ScrollViewer? _scrollViewer;

    public SearchPage(SearchViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindScrollViewer(ImageGrid);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchViewModel.IsPersonSelected) || !ViewModel.IsPersonSelected)
            return;

        // Defer until after the strip's Visibility has been applied and layout is complete.
        Dispatcher.BeginInvoke(ScrollSelectedPersonIntoView, DispatcherPriority.Background);
    }

    private void ScrollSelectedPersonIntoView()
    {
        int index = -1;
        for (int i = 0; i < ViewModel.People.Count; i++)
        {
            if (ViewModel.People[i].IsSelected) { index = i; break; }
        }
        if (index < 0) return;

        if (PeopleStrip.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            container.BringIntoView();
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

    private void PeopleStripScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        PeopleStripScroll.ScrollToHorizontalOffset(PeopleStripScroll.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
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
