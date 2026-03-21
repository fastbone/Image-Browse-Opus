using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageBrowse.Models;
using ImageBrowse.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ImageBrowse.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DatabaseService _db;
    private readonly ThumbnailService _thumbnailService;
    private readonly FolderThumbnailService _folderThumbnailService;
    private readonly MetadataService _metadataService;
    private readonly ImageLoadingService _imageLoadingService;

    public DatabaseService Database => _db;
    public SettingsService Settings { get; }

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _thumbnailSize = 180;
    [ObservableProperty] private ImageItem? _selectedItem;
    [ObservableProperty] private bool _isFullscreenActive;
    [ObservableProperty] private int _selectedIndex = -1;
    [ObservableProperty] private SortField _currentSortField = SortField.FileName;
    [ObservableProperty] private SortDirection _currentSortDirection = SortDirection.Ascending;
    [ObservableProperty] private bool _isFolderTreeVisible = true;
    [ObservableProperty] private bool _hasCustomFolderSort;

    public ObservableCollection<ImageItem> Images { get; } = [];
    public ObservableCollection<ImageItem> SortedImages { get; } = [];

    private List<ImageItem> _allImages = [];
    private CancellationTokenSource? _loadCts;
    private bool _suppressSortSave;

    public MainViewModel()
    {
        _db = new DatabaseService();
        Settings = new SettingsService(_db);
        _thumbnailService = new ThumbnailService(_db);
        _folderThumbnailService = new FolderThumbnailService(_db);
        _metadataService = new MetadataService(_db);
        _imageLoadingService = new ImageLoadingService();

        _isDarkTheme = Settings.IsDarkTheme;
        _thumbnailSize = Settings.ThumbnailSize;
        _isFolderTreeVisible = Settings.IsFolderTreeVisible;
        _currentSortField = Settings.DefaultSortField;
        _currentSortDirection = Settings.DefaultSortDirection;

        _thumbnailService.ThumbnailReady += OnThumbnailReady;
        _folderThumbnailService.FolderThumbnailReady += OnFolderThumbnailReady;
    }

    public async Task NavigateToFolder(string path)
    {
        if (!Directory.Exists(path)) return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _thumbnailService.CancelAll();
        _folderThumbnailService.CancelAll();

        IsLoading = true;
        CurrentPath = path;
        StatusText = "Loading...";
        Images.Clear();
        SortedImages.Clear();
        _allImages.Clear();
        SelectedItem = null;
        SelectedIndex = -1;

        LoadSortPreferenceForFolder(path);

        try
        {
            var (folders, files) = await Task.Run(() =>
            {
                var folderItems = Directory.GetDirectories(path)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .Select(d =>
                    {
                        ct.ThrowIfCancellationRequested();
                        int imgCount = ImageLoadingService.GetSupportedFiles(d.FullName).Take(100).Count();
                        int subfolderCount = 0;
                        try { subfolderCount = Directory.GetDirectories(d.FullName).Length; } catch { }
                        return new ImageItem
                        {
                            FilePath = d.FullName,
                            FileName = d.Name,
                            Extension = "",
                            FileSize = 0,
                            DateModified = d.LastWriteTime,
                            DateCreated = d.CreationTime,
                            IsFolder = true,
                            FolderImageCount = imgCount,
                            FolderSubfolderCount = subfolderCount
                        };
                    })
                    .ToList();

                var fileItems = ImageLoadingService.GetSupportedFiles(path)
                    .Select(f =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var fi = new FileInfo(f);
                        return new ImageItem
                        {
                            FilePath = f,
                            FileName = fi.Name,
                            Extension = fi.Extension.ToUpperInvariant().TrimStart('.'),
                            FileSize = fi.Length,
                            DateModified = fi.LastWriteTime,
                            DateCreated = fi.CreationTime,
                            Rating = _db.GetRating(f),
                            IsTagged = _db.GetTagged(f)
                        };
                    })
                    .ToList();

                return (folderItems, fileItems);
            }, ct);

            if (ct.IsCancellationRequested) return;

            _allImages = [.. folders, .. files];

            await Task.Run(() =>
            {
                foreach (var item in _allImages.Where(i => !i.IsFolder))
                {
                    ct.ThrowIfCancellationRequested();
                    _metadataService.LoadMetadata(item);

                    var dims = _db.GetCachedDimensions(item.FilePath, item.DateModified);
                    if (dims is not null && item.ImageWidth == 0)
                    {
                        item.ImageWidth = dims.Value.Width;
                        item.ImageHeight = dims.Value.Height;
                    }
                }
            }, ct);

            if (ct.IsCancellationRequested) return;

            ApplySortAndPopulate();

            int imageCount = _allImages.Count(i => !i.IsFolder);
            int folderCount = _allImages.Count(i => i.IsFolder);
            StatusText = folderCount > 0
                ? $"{folderCount:N0} folder{(folderCount != 1 ? "s" : "")}, {imageCount:N0} image{(imageCount != 1 ? "s" : "")}"
                : $"{imageCount:N0} image{(imageCount != 1 ? "s" : "")}";

            foreach (var item in SortedImages)
            {
                if (ct.IsCancellationRequested) break;

                if (item.IsFolder)
                {
                    var cached = _folderThumbnailService.GetCachedThumbnail(item.FilePath, item.DateModified);
                    if (cached is not null)
                    {
                        item.Thumbnail = cached;
                    }
                    else
                    {
                        item.IsThumbnailLoading = true;
                        _folderThumbnailService.RequestThumbnail(item.FilePath, item.DateModified);
                    }
                }
                else
                {
                    var cached = _thumbnailService.GetCachedThumbnail(item.FilePath, item.DateModified);
                    if (cached is not null)
                    {
                        item.Thumbnail = cached;
                    }
                    else
                    {
                        item.IsThumbnailLoading = true;
                        _thumbnailService.RequestThumbnail(item.FilePath, item.DateModified, item.FileSize);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadSortPreferenceForFolder(string path)
    {
        _suppressSortSave = true;
        try
        {
            var folderPref = Settings.GetFolderSort(path);
            if (folderPref is not null)
            {
                CurrentSortField = folderPref.Value.Field;
                CurrentSortDirection = folderPref.Value.Direction;
                HasCustomFolderSort = true;
            }
            else
            {
                CurrentSortField = Settings.DefaultSortField;
                CurrentSortDirection = Settings.DefaultSortDirection;
                HasCustomFolderSort = false;
            }
        }
        finally
        {
            _suppressSortSave = false;
        }
    }

    private void ApplySortAndPopulate()
    {
        var sorted = ApplySort(_allImages);
        SortedImages.Clear();
        foreach (var item in sorted)
            SortedImages.Add(item);
    }

    private IEnumerable<ImageItem> ApplySort(IEnumerable<ImageItem> items)
    {
        var folders = items.Where(i => i.IsFolder)
            .OrderBy(i => i.FileName, Helpers.NaturalSortComparer.Instance);

        var images = items.Where(i => !i.IsFolder);
        var sortedImages = CurrentSortField switch
        {
            SortField.FileName => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.FileName, Helpers.NaturalSortComparer.Instance)
                : images.OrderByDescending(i => i.FileName, Helpers.NaturalSortComparer.Instance),
            SortField.DateModified => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.DateModified)
                : images.OrderByDescending(i => i.DateModified),
            SortField.DateCreated => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.DateCreated)
                : images.OrderByDescending(i => i.DateCreated),
            SortField.DateTaken => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.DateTaken ?? DateTime.MaxValue)
                : images.OrderByDescending(i => i.DateTaken ?? DateTime.MinValue),
            SortField.FileSize => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.FileSize)
                : images.OrderByDescending(i => i.FileSize),
            SortField.Dimensions => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => (long)i.ImageWidth * i.ImageHeight)
                : images.OrderByDescending(i => (long)i.ImageWidth * i.ImageHeight),
            SortField.FileType => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.Extension).ThenBy(i => i.FileName, Helpers.NaturalSortComparer.Instance)
                : images.OrderByDescending(i => i.Extension).ThenByDescending(i => i.FileName, Helpers.NaturalSortComparer.Instance),
            SortField.Rating => CurrentSortDirection == SortDirection.Ascending
                ? images.OrderBy(i => i.Rating).ThenBy(i => i.FileName, Helpers.NaturalSortComparer.Instance)
                : images.OrderByDescending(i => i.Rating).ThenByDescending(i => i.FileName, Helpers.NaturalSortComparer.Instance),
            _ => images.OrderBy(i => i.FileName, Helpers.NaturalSortComparer.Instance)
        };

        return folders.Concat(sortedImages);
    }

    partial void OnCurrentSortFieldChanged(SortField value)
    {
        ApplySortAndPopulate();
        SaveCurrentFolderSort();
    }

    partial void OnCurrentSortDirectionChanged(SortDirection value)
    {
        ApplySortAndPopulate();
        SaveCurrentFolderSort();
    }

    private void SaveCurrentFolderSort()
    {
        if (_suppressSortSave || string.IsNullOrEmpty(CurrentPath)) return;
        Settings.SetFolderSort(CurrentPath, CurrentSortField, CurrentSortDirection);
        HasCustomFolderSort = true;
    }

    partial void OnIsDarkThemeChanged(bool value) => Settings.IsDarkTheme = value;
    partial void OnThumbnailSizeChanged(int value) => Settings.ThumbnailSize = value;
    partial void OnIsFolderTreeVisibleChanged(bool value) => Settings.IsFolderTreeVisible = value;

    [RelayCommand]
    private void ToggleSortDirection()
    {
        CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;
    }

    [RelayCommand]
    private void SetSortField(SortField field)
    {
        if (CurrentSortField == field)
            ToggleSortDirection();
        else
            CurrentSortField = field;
    }

    [RelayCommand]
    private void ResetFolderSort()
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        Settings.ClearFolderSort(CurrentPath);

        _suppressSortSave = true;
        try
        {
            CurrentSortField = Settings.DefaultSortField;
            CurrentSortDirection = Settings.DefaultSortDirection;
            HasCustomFolderSort = false;
        }
        finally
        {
            _suppressSortSave = false;
        }
        ApplySortAndPopulate();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
    }

    [RelayCommand]
    private void ToggleFolderTree()
    {
        IsFolderTreeVisible = !IsFolderTreeVisible;
    }

    [RelayCommand]
    private void SetRating(string? ratingStr)
    {
        if (SelectedItem is null || !int.TryParse(ratingStr, out int rating)) return;
        if (SelectedItem.Rating == rating)
            rating = 0;
        SelectedItem.Rating = rating;
        _db.SetRating(SelectedItem.FilePath, rating);
    }

    [RelayCommand]
    private void ToggleTag()
    {
        if (SelectedItem is null) return;
        SelectedItem.IsTagged = !SelectedItem.IsTagged;
        _db.SetTagged(SelectedItem.FilePath, SelectedItem.IsTagged);
    }

    [RelayCommand]
    private void IncreaseThumbnailSize()
    {
        ThumbnailSize = Math.Min(ThumbnailSize + 40, 400);
    }

    [RelayCommand]
    private void DecreaseThumbnailSize()
    {
        ThumbnailSize = Math.Max(ThumbnailSize - 40, 80);
    }

    public void EnterFullscreen()
    {
        if (SelectedItem?.IsFolder == true) return;

        if (SelectedItem is null && SortedImages.Count > 0)
        {
            var first = SortedImages.FirstOrDefault(i => !i.IsFolder);
            if (first is null) return;
            SelectedIndex = SortedImages.IndexOf(first);
            SelectedItem = first;
        }
        if (SelectedItem is not null)
            IsFullscreenActive = true;
    }

    public void ExitFullscreen()
    {
        IsFullscreenActive = false;
    }

    public ImageItem? GetNextImage()
    {
        if (SortedImages.Count == 0) return null;
        for (int idx = SelectedIndex + 1; idx < SortedImages.Count; idx++)
        {
            if (!SortedImages[idx].IsFolder)
            {
                SelectedIndex = idx;
                SelectedItem = SortedImages[idx];
                return SelectedItem;
            }
        }
        return SelectedItem;
    }

    public ImageItem? GetPreviousImage()
    {
        if (SortedImages.Count == 0) return null;
        for (int idx = SelectedIndex - 1; idx >= 0; idx--)
        {
            if (!SortedImages[idx].IsFolder)
            {
                SelectedIndex = idx;
                SelectedItem = SortedImages[idx];
                return SelectedItem;
            }
        }
        return SelectedItem;
    }

    public ImageItem? GetFirstImage()
    {
        if (SortedImages.Count == 0) return null;
        for (int idx = 0; idx < SortedImages.Count; idx++)
        {
            if (!SortedImages[idx].IsFolder)
            {
                SelectedIndex = idx;
                SelectedItem = SortedImages[idx];
                return SelectedItem;
            }
        }
        return null;
    }

    public ImageItem? GetLastImage()
    {
        if (SortedImages.Count == 0) return null;
        for (int idx = SortedImages.Count - 1; idx >= 0; idx--)
        {
            if (!SortedImages[idx].IsFolder)
            {
                SelectedIndex = idx;
                SelectedItem = SortedImages[idx];
                return SelectedItem;
            }
        }
        return null;
    }

    public void RefreshThumbnail(ImageItem item)
    {
        if (item.IsFolder) return;
        _db.DeleteThumbnail(item.FilePath);
        item.Thumbnail = null;
        item.IsThumbnailLoading = true;
        _thumbnailService.RequestThumbnail(item.FilePath, item.DateModified, item.FileSize);

        RefreshParentFolderThumbnail(item.FilePath);
    }

    public void RefreshParentFolderThumbnail(string filePath)
    {
        var parentDir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(parentDir)) return;

        var folderItem = SortedImages.FirstOrDefault(i => i.IsFolder &&
            string.Equals(i.FilePath, parentDir, StringComparison.OrdinalIgnoreCase));
        if (folderItem is null) return;

        _db.DeleteThumbnail(parentDir);

        try
        {
            var dirInfo = new DirectoryInfo(parentDir);
            if (dirInfo.Exists)
                folderItem.DateModified = dirInfo.LastWriteTime;
        }
        catch { }

        folderItem.Thumbnail = null;
        folderItem.IsThumbnailLoading = true;
        _folderThumbnailService.RequestThumbnail(parentDir, folderItem.DateModified);
    }

    public System.Windows.Media.Imaging.BitmapSource? LoadFullImage(string filePath)
    {
        return _imageLoadingService.LoadFullImage(filePath);
    }

    public System.Windows.Media.Imaging.BitmapSource? LoadScreenImage(string filePath, int maxDimension)
    {
        return _imageLoadingService.LoadFullImage(filePath, maxDimension);
    }

    private void OnThumbnailReady(string filePath, System.Windows.Media.Imaging.BitmapSource thumbnail, int width, int height)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var item = SortedImages.FirstOrDefault(i => i.FilePath == filePath);
            if (item is not null)
            {
                item.Thumbnail = thumbnail;
                item.IsThumbnailLoading = false;
                if (item.ImageWidth == 0 && width > 0) item.ImageWidth = width;
                if (item.ImageHeight == 0 && height > 0) item.ImageHeight = height;
            }
        });
    }

    private void OnFolderThumbnailReady(string folderPath, System.Windows.Media.Imaging.BitmapSource thumbnail)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var item = SortedImages.FirstOrDefault(i => i.FilePath == folderPath && i.IsFolder);
            if (item is not null)
            {
                item.Thumbnail = thumbnail;
                item.IsThumbnailLoading = false;
            }
        });
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _thumbnailService.ThumbnailReady -= OnThumbnailReady;
        _thumbnailService.Dispose();
        _folderThumbnailService.FolderThumbnailReady -= OnFolderThumbnailReady;
        _folderThumbnailService.Dispose();
        _db.Dispose();
    }
}
