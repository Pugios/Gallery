using Gallery2.Models;
using Gallery2.Views.Pages;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Navigation;
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

    private INavigationService _navigationService;
    private System.Collections.IList _menuItems = new System.Collections.ArrayList();
    [ObservableProperty]
    private string _applicationTitle = "Gallery";

    public MainWindowViewModel(GalleryState galleryState, HeaderState headerState,INavigationService navigationService)
    {
        _galleryState = galleryState;
        _headerState = headerState;
        _navigationService = navigationService;

        _galleryState.ImportedFolders.CollectionChanged += OnImportedFoldersChanged;
    }

    private void AddFolderNavItem(string folderPath)
    {
        var folderItem = new NavigationViewItem()
        {
            Content = Path.GetFileName(folderPath),
            TargetPageTag = folderPath,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Folder24 },
            TargetPageType = typeof(GalleryPage)
        };
        folderItem.PreviewMouseLeftButtonDown += (_, _) => _galleryState.ActiveFolder = folderPath;
        _menuItems.Add(folderItem);
    }

    private void OnImportedFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (string folderPath in e.NewItems)
        {
            AddFolderNavItem(folderPath);
            _navigationService.Navigate(folderPath);
        }
    }

    [RelayCommand]
    private void ClearActiveFolder() => _galleryState.ActiveFolder = null;
    [RelayCommand]
    private void OnAddFolderFromNav()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder of images" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        
        _galleryState.ActiveFolder = path;

        if (!_galleryState.ImportedFolders.Contains(path))
            _galleryState.ImportedFolders.Add(path);

    }

    public void SetMenuItems(System.Collections.IList menuItems)
    {
        _menuItems = menuItems;

        foreach (var folder in _galleryState.ImportedFolders)
            AddFolderNavItem(folder);
    }
}
