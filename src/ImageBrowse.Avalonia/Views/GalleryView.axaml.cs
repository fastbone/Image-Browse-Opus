using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageBrowse.Helpers;
using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class GalleryView : UserControl
{
    public event Action<int>? SelectionCountChanged;

    private readonly HashSet<ImageItem> _selected = new(ReferenceEqualityComparer<ImageItem>.Instance);
    private int _rangeAnchorIndex;
    private Point _dragPressPoint;
    private bool _dragArmed;
    private PointerPressedEventArgs? _dragPressArgs;
    private TextBox? _renameBox;
    private TextBlock? _renameLabel;
    private Grid? _renameHostGrid;

    private static readonly ReferenceEqualityComparer<ImageItem> RefEq = ReferenceEqualityComparer<ImageItem>.Instance;

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    public GalleryView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ApplyRepeaterLayout();
                GalleryScroll.ScrollChanged += GalleryScroll_OnScrollChanged;
                RequestThumbnailsForCurrentScroll();
            }, DispatcherPriority.Loaded);
        };

        DataContextChanged += (_, _) =>
        {
            if (ViewModel is not null)
            {
                ViewModel.SortedImages.CollectionChanged += (_, _) =>
                {
                    UpdateEmptyState();
                    Dispatcher.UIThread.Post(RefreshSelectionVisuals, DispatcherPriority.Background);
                };
                ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
            }
        };
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.PropertyName == nameof(MainViewModel.IsLoading))
            UpdateEmptyState();
        else if (e.PropertyName == nameof(MainViewModel.ThumbnailSize))
        {
            ApplyRepeaterLayout();
            Dispatcher.UIThread.Post(RefreshSelectionVisuals, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedItem))
        {
            if (ViewModel.SelectedItem is { } si && !ReferenceEquals(_renameHostGrid?.DataContext, si))
            {
                CancelInlineRename();
                _selected.Clear();
                _selected.Add(si);
                RefreshSelectionVisuals();
                SelectionCountChanged?.Invoke(_selected.Count);
            }
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ApplyRepeaterLayout()
    {
        if (ViewModel is null) return;
        double w = Math.Max(40, ViewModel.ThumbnailSize + 12);
        double h = Math.Max(60, ViewModel.ThumbnailSize + 56);
        GalleryRepeater.Layout = new UniformGridLayout
        {
            Orientation = Orientation.Horizontal,
            MinItemWidth = w,
            MinItemHeight = h,
            ItemsStretch = UniformGridLayoutItemsStretch.Uniform,
        };
    }

    private void GalleryScroll_OnScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        RequestThumbnailsForCurrentScroll();

    private void RequestThumbnailsForCurrentScroll()
    {
        if (ViewModel is null || ViewModel.SortedImages.Count == 0) return;

        double cellW = ViewModel.ThumbnailSize + 16;
        double cellH = ViewModel.ThumbnailSize + 56;
        if (cellW <= 0 || cellH <= 0) return;

        int cols = Math.Max(1, (int)(GalleryScroll.Viewport.Width / cellW));
        int firstRow = Math.Max(0, (int)(GalleryScroll.Offset.Y / cellH));
        int rowCount = Math.Max(1, (int)Math.Ceiling(GalleryScroll.Viewport.Height / cellH) + 1);
        int first = firstRow * cols;
        int last = Math.Min(ViewModel.SortedImages.Count - 1, (firstRow + rowCount) * cols + cols - 1);
        ViewModel.RequestThumbnailsForVisibleRange(first, last);
    }

    private void UpdateEmptyState()
    {
        if (ViewModel is null) return;
        bool isEmpty = ViewModel.SortedImages.Count == 0 && !ViewModel.IsLoading
                       && !string.IsNullOrEmpty(ViewModel.CurrentPath);
        EmptyState.IsVisible = isEmpty;
    }

    private void GalleryMenuFlyout_Opened(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout mf || ViewModel is null) return;
        bool ops = ViewModel.Settings.FileOperationsEnabled;
        foreach (var o in mf.Items)
        {
            if (o is MenuItem mi && mi.Header is string h)
            {
                if (h is "Rename" or "Move to…" or "Copy to…" or "Delete")
                    mi.IsVisible = ops;
            }
        }
    }

    private void GalleryTile_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || sender is not Border border || border.DataContext is not ImageItem item)
            return;

        GalleryScroll.Focus();

        var props = e.GetCurrentPoint(border).Properties;
        if (props.IsRightButtonPressed)
        {
            ApplySelectionForPointer(item, e.KeyModifiers, isContext: true);
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        _dragArmed = ViewModel.Settings.FileOperationsEnabled;
        _dragPressPoint = e.GetPosition(null);
        _dragPressArgs = e;
        ApplySelectionForPointer(item, e.KeyModifiers, isContext: false);
    }

    private void GalleryTile_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragArmed || ViewModel is null || !ViewModel.Settings.FileOperationsEnabled) return;
        if (_dragPressArgs is null || sender is not Border) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var now = e.GetPosition(null);
        if (Math.Abs(now.X - _dragPressPoint.X) < 4 && Math.Abs(now.Y - _dragPressPoint.Y) < 4)
            return;

        _dragArmed = false;
        _ = StartDragFromGalleryAsync(_dragPressArgs, e);
    }

    private void GalleryTile_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragArmed = false;
        _dragPressArgs = null;
    }

    private async Task StartDragFromGalleryAsync(PointerPressedEventArgs pressArgs, PointerEventArgs _)
    {
        if (ViewModel is null) return;
        var items = GetSelectedItemsForOps();
        if (items.Count == 0) return;

        var paths = items.Select(i => i.FilePath).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var storage = top.StorageProvider;
        var dt = new DataTransfer();
        foreach (var path in paths)
        {
            IStorageItem? entry = File.Exists(path)
                ? await storage.TryGetFileFromPathAsync(path)
                : await storage.TryGetFolderFromPathAsync(path);
            if (entry is not null)
                dt.Add(DataTransferItem.CreateFile(entry));
        }

        if (dt.Items.Count == 0) return;

        GalleryInternalDragState.Items = items;
        try
        {
            await DragDrop.DoDragDropAsync(pressArgs, dt,
                DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch
        {
            /* drag cancelled */
        }
        finally
        {
            GalleryInternalDragState.Items = null;
        }
    }

    private void GalleryTile_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null || sender is not Border b || b.DataContext is not ImageItem item) return;
        if (item.IsFolder || item.IsParentFolder)
            ViewModel.NavigateToFolder(item.FilePath);
        else
            ViewModel.EnterFullscreen();
    }

    private void GalleryScroll_DoubleTapped(object? sender, TappedEventArgs e)
    {
        /* handled on tile */
    }

    private void ApplySelectionForPointer(ImageItem item, KeyModifiers mods, bool isContext)
    {
        if (ViewModel is null) return;

        var ctrl = (mods & KeyModifiers.Control) != 0;
        var shift = (mods & KeyModifiers.Shift) != 0;

        if (shift && ViewModel.SortedImages.Count > 0)
        {
            int end = ViewModel.SortedImages.IndexOf(item);
            if (end < 0) return;
            int start = Math.Clamp(_rangeAnchorIndex, 0, ViewModel.SortedImages.Count - 1);
            if (start > end) (start, end) = (end, start);
            _selected.Clear();
            for (int i = start; i <= end; i++)
                _selected.Add(ViewModel.SortedImages[i]);
        }
        else if (ctrl)
        {
            if (!_selected.Add(item))
                _selected.Remove(item);
            _rangeAnchorIndex = ViewModel.SortedImages.IndexOf(item);
        }
        else
        {
            _selected.Clear();
            _selected.Add(item);
            _rangeAnchorIndex = ViewModel.SortedImages.IndexOf(item);
        }

        if (_selected.Contains(item))
        {
            ViewModel.SelectedItem = item;
            ViewModel.SelectedIndex = ViewModel.SortedImages.IndexOf(item);
        }
        else if (_selected.Count > 0)
        {
            var last = _selected.Last();
            ViewModel.SelectedItem = last;
            ViewModel.SelectedIndex = ViewModel.SortedImages.IndexOf(last);
        }

        RefreshSelectionVisuals();
        SelectionCountChanged?.Invoke(_selected.Count);
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var b in GalleryRepeater.GetVisualDescendants().OfType<Border>())
        {
            if (!b.Classes.Contains("galleryTile")) continue;
            if (b.DataContext is not ImageItem it) continue;
            b.Classes.Set("selected", _selected.Contains(it));
        }
    }

    private List<ImageItem> GetSelectedItemsForOps()
    {
        if (ViewModel is null) return [];
        if (_selected.Count > 0)
            return _selected.Where(i => !i.IsParentFolder).ToList();
        if (ViewModel.SelectedItem is { } one && !one.IsParentFolder)
            return [one];
        return [];
    }

    private void GalleryScroll_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        switch (e.Key)
        {
            case Key.Enter:
            {
                var item = ViewModel.SelectedItem;
                if (item is null) break;
                if (item.IsFolder || item.IsParentFolder)
                    ViewModel.NavigateToFolder(item.FilePath);
                else
                    ViewModel.EnterFullscreen();
                e.Handled = true;
                break;
            }
            case Key.Back:
            {
                var parent = Path.GetDirectoryName(ViewModel.CurrentPath);
                if (!string.IsNullOrEmpty(parent))
                    ViewModel.NavigateToFolder(parent);
                e.Handled = true;
                break;
            }
            case Key.Delete:
                _ = DeleteSelectedImagesAsync();
                e.Handled = true;
                break;
            case Key.F2:
                BeginRenameSelected();
                e.Handled = true;
                break;
            case Key.R when !ctrl:
                RotateSelectedImages();
                e.Handled = true;
                break;
            case Key.Right:
            {
                int cur = Math.Max(0, ViewModel.SelectedIndex);
                if (cur < ViewModel.SortedImages.Count - 1)
                {
                    var next = ViewModel.SortedImages[cur + 1];
                    _selected.Clear();
                    _selected.Add(next);
                    ViewModel.SelectedIndex = cur + 1;
                    ViewModel.SelectedItem = next;
                    _rangeAnchorIndex = cur + 1;
                    ScrollToItem(next);
                    RefreshSelectionVisuals();
                    SelectionCountChanged?.Invoke(_selected.Count);
                    e.Handled = true;
                }
                break;
            }
            case Key.Left:
            {
                int cur = ViewModel.SelectedIndex;
                if (cur > 0)
                {
                    var prev = ViewModel.SortedImages[cur - 1];
                    _selected.Clear();
                    _selected.Add(prev);
                    ViewModel.SelectedIndex = cur - 1;
                    ViewModel.SelectedItem = prev;
                    _rangeAnchorIndex = cur - 1;
                    ScrollToItem(prev);
                    RefreshSelectionVisuals();
                    SelectionCountChanged?.Invoke(_selected.Count);
                    e.Handled = true;
                }
                break;
            }
        }
    }

    public void ScrollToItem(ImageItem item)
    {
        if (ViewModel is null) return;
        int idx = ViewModel.SortedImages.IndexOf(item);
        if (idx < 0) return;

        double cellW = ViewModel.ThumbnailSize + 16;
        double cellH = ViewModel.ThumbnailSize + 56;
        if (cellW <= 0 || cellH <= 0) return;

        int cols = Math.Max(1, (int)(GalleryScroll.Viewport.Width / cellW));
        int row = idx / cols;
        double targetY = row * cellH - GalleryScroll.Viewport.Height / 2 + cellH / 2;
        double maxY = Math.Max(0, GalleryScroll.Extent.Height - GalleryScroll.Viewport.Height);
        targetY = Math.Clamp(targetY, 0, maxY);
        GalleryScroll.Offset = new Vector(GalleryScroll.Offset.X, targetY);
    }

    public void FocusGallery() => GalleryScroll.Focus();

    #region Context Menu

    private void ContextOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is null) return;
        var item = ViewModel.SelectedItem;
        if (item.IsFolder || item.IsParentFolder)
        {
            string? returnTo = item.IsParentFolder ? Path.GetFileName(ViewModel.CurrentPath) : null;
            ViewModel.NavigateToFolder(item.FilePath, returnTo);
        }
        else
            ViewModel.EnterFullscreen();
    }

    private void ContextRate1_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("1");
    private void ContextRate2_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("2");
    private void ContextRate3_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("3");
    private void ContextRate4_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("4");
    private void ContextRate5_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("5");
    private void ContextRate0_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("0");
    private void ContextToggleTag_Click(object? sender, RoutedEventArgs e) => ViewModel?.ToggleTagCommand.Execute(null);
    private void ContextRename_Click(object? sender, RoutedEventArgs e) => BeginRenameSelected();
    private async void ContextMove_Click(object? sender, RoutedEventArgs e) => await MoveOrCopySelectedAsync(move: true);
    private async void ContextCopy_Click(object? sender, RoutedEventArgs e) => await MoveOrCopySelectedAsync(move: false);
    private void ContextDelete_Click(object? sender, RoutedEventArgs e) => _ = DeleteSelectedImagesAsync();

    private void ContextShowInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is null) return;
        var path = ViewModel.SelectedItem.FilePath;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", $"-R \"{path}\"");
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", $"\"{Path.GetDirectoryName(path)}\"");
        }
        catch { }
    }

    private void ContextRefreshThumb_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is not null)
            ViewModel.RefreshThumbnail(ViewModel.SelectedItem);
    }

    #endregion

    #region File operations

    private void BeginRenameSelected()
    {
        if (ViewModel is null || !ViewModel.Settings.FileOperationsEnabled) return;
        var item = ViewModel.SelectedItem;
        if (item is null || item.IsParentFolder) return;

        CancelInlineRename();

        var tile = FindGalleryTile(item);
        if (tile is null || tile.Child is not Grid grid) return;

        _renameHostGrid = grid;
        _renameLabel = grid.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(tb => tb.Classes.Contains("gallery-filename"));
        if (_renameLabel is null) return;

        _renameLabel.IsVisible = false;
        _renameBox = new TextBox
        {
            Text = item.FileName,
            FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(_renameBox, 1);
        grid.Children.Add(_renameBox);
        _renameBox.Focus();
        _renameBox.SelectAll();
        _renameBox.LostFocus += RenameBox_LostFocus;
        _renameBox.KeyDown += RenameBox_KeyDown;
    }

    private Border? FindGalleryTile(ImageItem item) =>
        GalleryRepeater.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("galleryTile") && ReferenceEquals(b.DataContext, item));

    private void RenameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitInlineRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelInlineRename();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object? sender, RoutedEventArgs e) => CommitInlineRename();

    private void CommitInlineRename()
    {
        if (ViewModel is null || _renameBox is null || _renameLabel is null || _renameHostGrid is null) return;
        if (_renameHostGrid.DataContext is not ImageItem item) return;

        var newName = _renameBox.Text?.Trim();
        CancelInlineRename();
        if (string.IsNullOrEmpty(newName)) return;

        var (ok, err) = FileOperationService.Rename(item.FilePath, newName);
        if (ok)
            ViewModel.RenameItem(item, newName);
        else if (TopLevel.GetTopLevel(this) is Window w)
            _ = DialogUtil.ShowMessageAsync(w, err ?? "Rename failed.", "Rename");
    }

    private void CancelInlineRename()
    {
        if (_renameBox is not null)
        {
            _renameBox.LostFocus -= RenameBox_LostFocus;
            _renameBox.KeyDown -= RenameBox_KeyDown;
            _renameHostGrid?.Children.Remove(_renameBox);
            _renameBox = null;
        }
        if (_renameLabel is not null)
        {
            _renameLabel.IsVisible = true;
            _renameLabel = null;
        }
        _renameHostGrid = null;
    }

    private async Task DeleteSelectedImagesAsync()
    {
        if (ViewModel is null || !ViewModel.Settings.FileOperationsEnabled) return;
        var items = GetSelectedItemsForOps();
        if (items.Count == 0) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        bool hasFolders = items.Any(i => i.IsFolder);

        if (ViewModel.Settings.ConfirmBeforeDelete)
        {
            string msg;
            if (items.Count == 1)
            {
                msg = items[0].IsFolder
                    ? $"Move folder \"{items[0].FileName}\" and all its contents to the Recycle Bin?"
                    : $"Move \"{items[0].FileName}\" to the Recycle Bin?";
            }
            else
            {
                int folderCount = items.Count(i => i.IsFolder);
                int fileCount = items.Count - folderCount;
                var parts = new List<string>();
                if (fileCount > 0) parts.Add($"{fileCount} file{(fileCount != 1 ? "s" : "")}");
                if (folderCount > 0) parts.Add($"{folderCount} folder{(folderCount != 1 ? "s" : "")}");
                msg = $"Move {string.Join(" and ", parts)} to the Recycle Bin?";
            }

            var ok = await DialogUtil.ShowYesNoAsync(owner,
                msg + "\n\nYou can disable this confirmation in Settings.",
                "Confirm Delete");
            if (!ok) return;
        }

        ExecuteDelete(items, hasFolders);
    }

    private void ExecuteDelete(List<ImageItem> items, bool hasFolders)
    {
        if (ViewModel is null) return;

        int currentIdx = ViewModel.SelectedIndex;
        var deleted = new List<ImageItem>();

        foreach (var item in items)
        {
            if (FileOperationService.MoveToRecycleBin(item.FilePath))
                deleted.Add(item);
        }

        if (deleted.Count == 0) return;

        foreach (var d in deleted)
            _selected.Remove(d);

        if (hasFolders)
            ViewModel.OnItemsMoved(deleted);
        else
            ViewModel.RemoveImages(deleted);

        if (ViewModel.SortedImages.Count > 0)
        {
            int newIdx = Math.Min(currentIdx, ViewModel.SortedImages.Count - 1);
            ViewModel.SelectedIndex = newIdx;
            ViewModel.SelectedItem = ViewModel.SortedImages[newIdx];
            _selected.Clear();
            _selected.Add(ViewModel.SelectedItem);
            RefreshSelectionVisuals();
            SelectionCountChanged?.Invoke(_selected.Count);
        }
    }

    private async Task MoveOrCopySelectedAsync(bool move)
    {
        if (ViewModel is null || !ViewModel.Settings.FileOperationsEnabled) return;
        var items = GetSelectedItemsForOps().Where(i => !i.IsFolder).ToList();
        if (items.Count == 0) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = move ? "Move to folder" : "Copy to folder",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        string dest = folders[0].Path.LocalPath;
        var paths = items.Select(i => i.FilePath).ToArray();

        bool ok = move
            ? FileOperationService.MoveItems(paths, dest, default)
            : FileOperationService.CopyItems(paths, dest, default);

        if (ok && move)
            ViewModel.OnItemsMoved(items);

        if (!ok)
        {
            var owner = top as Window;
            if (owner is not null)
                await DialogUtil.ShowMessageAsync(owner, "The operation could not be completed.", move ? "Move" : "Copy");
        }
        else
            ViewModel.RefreshCurrentFolder();
    }

    private async void RotateSelectedImages()
    {
        if (ViewModel is null) return;
        var items = GetSelectedItemsForOps().Where(i => !i.IsFolder && !i.IsVideo).ToList();
        if (items.Count == 0) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        int failed = 0;
        foreach (var item in items)
        {
            try
            {
                var (newW, newH) = await Task.Run(() => ImageRotationService.RotateClockwise90(item.FilePath));
                var fi = new FileInfo(item.FilePath);
                item.ImageWidth = newW;
                item.ImageHeight = newH;
                item.DateModified = fi.LastWriteTime;
                item.FileSize = fi.Length;
                ViewModel.RefreshThumbnail(item);
            }
            catch
            {
                failed++;
            }
        }

        if (failed > 0 && owner is not null)
        {
            await DialogUtil.ShowMessageAsync(owner,
                $"Failed to rotate {failed} file{(failed != 1 ? "s" : "")}. The file may be read-only or in use.",
                "Rotation Error");
        }
    }

    #endregion
}
