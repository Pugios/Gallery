# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build Gallery2.csproj
dotnet run
```

Target framework: `net10.0-windows`. Requires Windows (WPF + Windows Shell COM interop).

## Architecture Overview

**Gallery2** is a WPF desktop photo/video browser. It uses Microsoft.Extensions.Hosting for DI, WPF-UI (Fluent Design) for the UI framework, and CommunityToolkit.Mvvm for MVVM.

### Core pattern: MVVM + DI

All services and ViewModels are registered as singletons in `App.xaml.cs`. The host starts via `ApplicationHostService`, which restores persisted folders and launches `MainWindow`.

### Shared state objects (singletons)

- **`GalleryState`** — owns the `ImportedFolders` collection and `SelectedGroupMode`. Both `MainWindowViewModel` and `GalleryViewModel` react to its `CollectionChanged` events.
- **`HeaderState`** — drives the dynamic title/subtitle/icon in the main window header.
- **`PersistenceService`** — reads/writes `%APPDATA%\Gallery\folders.txt` and `metadata_cache.csv` (pipe-delimited EXIF cache).

### Picture loading pipeline (GalleryViewModel)

1. `OnNavigatedToAsync()` → `LoadFoldersAsync()` on a background thread
2. Enumerate supported files (`.jpg/.png/.mp4/…`)
3. Load `metadata_cache.csv`; extract EXIF DateTaken + GPS for uncached files via **MetadataExtractor**
4. Save updated cache, create `PictureItem` objects sorted by `DateTaken`
5. Build an `ICollectionView` with group descriptions driven by `SelectedGroupMode` (Month/Week/Day)
6. Thumbnail loading: max 4 parallel tasks via `SemaphoreSlim`; tries **ShellThumbnailService** (Windows Shell COM) first, falls back to `BitmapImage`; cached with `WeakReference<BitmapSource>` to allow GC under memory pressure

### Navigation

WPF-UI's `INavigationWindow` / `INavigableView` / `INavigationAware` pattern. Each page ViewModel implements `OnNavigatedTo`/`OnNavigatedFrom`. `MainWindow` holds the `NavigationView`; `GalleryPage` and `SettingsPage` are the two navigable views.

### Key classes

| File | Role |
|---|---|
| `App.xaml.cs` | DI registration, host startup |
| `Services/ApplicationHostService.cs` | Orchestrates activation, restores folders |
| `Services/PersistenceService.cs` | File I/O for folders + metadata cache |
| `Services/ShellThumbnailService.cs` | COM P/Invoke for native Windows thumbnails |
| `Models/GalleryState.cs` | App-wide mutable state |
| `ViewModels/GalleryViewModel.cs` | Core loading/grouping logic |
| `Converters/DateGroupConverter.cs` | Converts `DateTaken` → group header string |

## Dependencies

- **WPF-UI** — Fluent/Mica theming, NavigationView, modern controls
- **MetadataExtractor** — EXIF/GPS extraction from images
- **CommunityToolkit.Mvvm** — `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`
- **Microsoft.Extensions.Hosting** — DI container and app lifetime
