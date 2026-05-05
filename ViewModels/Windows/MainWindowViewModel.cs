using Gallery2.Helpers;
using Gallery2.Models;
using Gallery2.Services;
using Gallery2.Views.Pages;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace Gallery2.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{

    [ObservableProperty]
    private ObservableCollection<MenuItem> _trayMenuItems = new()
    {
        new MenuItem { Header = "Home", Tag = "tray_home" }
    };

    private readonly GalleryState _galleryState;
    public GalleryState GalleryState => _galleryState;

    private readonly HeaderState _headerState;
    public HeaderState HeaderState => _headerState;

    private readonly FaceIndexingService _faceIndexingService;
    private INavigationService _navigationService;
    private System.Collections.IList _menuItems = new System.Collections.ArrayList();
    private readonly Dictionary<string, NavigationViewItem> _folderNavItems = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _applicationTitle = "Gallery";

    public MainWindowViewModel(GalleryState galleryState, HeaderState headerState, INavigationService navigationService, FaceIndexingService faceIndexingService)
    {
        _galleryState = galleryState;
        _headerState = headerState;
        _navigationService = navigationService;
        _faceIndexingService = faceIndexingService;

        _galleryState.ImportedFolders.CollectionChanged += OnImportedFoldersChanged;
    }

    private void AddFolderNavItem(string folderPath)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(NavigationViewItem) },
            Converter = new SubtractConverter(),
            ConverterParameter = "44"
        });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var label = new System.Windows.Controls.TextBlock
        {
            Text = Path.GetFileName(folderPath),
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Controls.Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var removeBtn = new Button
        {
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center,
            Appearance = ControlAppearance.Transparent,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss16 },
            ToolTip = "Remove folder",
        };
        removeBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await RemoveFolderAsync(folderPath);
        };
        System.Windows.Controls.Grid.SetColumn(removeBtn, 1);
        grid.Children.Add(removeBtn);

        var folderItem = new NavigationViewItem
        {
            Content = grid,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            TargetPageTag = folderPath,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Folder24 },
            TargetPageType = typeof(GalleryPage)
        };
        folderItem.PreviewMouseLeftButtonDown += (_, _) => _galleryState.ActiveFolder = folderPath;
        _menuItems.Add(folderItem);
        _folderNavItems[folderPath] = folderItem;
    }

    private void OnImportedFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (string folderPath in e.NewItems)
            {
                AddFolderNavItem(folderPath);
                _navigationService.Navigate(folderPath);
            }
        }
        if (e.OldItems is not null)
        {
            foreach (string folderPath in e.OldItems)
            {
                if (_folderNavItems.Remove(folderPath, out var item))
                    _menuItems.Remove(item);
            }
        }
    }

    private async Task RemoveFolderAsync(string folderPath)
    {
        // Remove first so any subsequent navigation sees the updated collection
        _galleryState.ImportedFolders.Remove(folderPath);

        if (string.Equals(_galleryState.ActiveFolder, folderPath, StringComparison.OrdinalIgnoreCase))
        {
            _galleryState.ActiveFolder = null;
            _navigationService.Navigate(typeof(GalleryPage));
        }

        await _faceIndexingService.RemoveFolderFromIndexAsync(folderPath);
    }

    [RelayCommand]
    private void ClearActiveFolder() => _galleryState.ActiveFolder = null;
    [RelayCommand]
    private void OnAddFolderFromNav()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder of images" };
        if (dialog.ShowDialog() != true) return;
        _galleryState.AddFolder(dialog.FolderName);
    }

    public void SetMenuItems(System.Collections.IList menuItems)
    {
        _menuItems = menuItems;

        foreach (var folder in _galleryState.ImportedFolders)
            AddFolderNavItem(folder);
    }
}
