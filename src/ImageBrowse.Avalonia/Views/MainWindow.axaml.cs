using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageBrowse.Helpers;
using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = null!;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly UpdateService _updateService = new();
    private readonly string? _startupPath;
    private bool _navigatingFromHistory;
    private bool _suppressTreeNavigation;
    private double _folderTreeWidth = 240;
    private ColumnDefinition? _folderTreeColDef;
    private bool _updateReadyToApply;
    private bool _addressEditMode;
    private TreeViewItem? _treeDropHighlight;

    public MainWindow() : this(null!, null) { }

    public MainWindow(MainViewModel vm, string? startupPath = null)
    {
        InitializeComponent();
        _vm = vm;
        _startupPath = startupPath;
        if (_vm is null) return;
        DataContext = _vm;

        _vm.PropertyChanged += ViewModel_PropertyChanged;
        _vm.FolderTreeRefreshRequested += () =>
        {
            PopulateRootTree();
            if (!string.IsNullOrEmpty(_vm.CurrentPath))
                SyncFolderTree(_vm.CurrentPath);
        };

        Gallery.SelectionCountChanged += count =>
        {
            SelectionCountText.Text = count > 1 ? $"{count} selected" : "";
        };

        Loaded += OnWindowLoaded;

        DragDrop.SetAllowDrop(FolderTree, true);
        DragDrop.AddDragOverHandler(FolderTree, FolderTree_DragOver);
        DragDrop.AddDropHandler(FolderTree, FolderTree_Drop);
        DragDrop.AddDragLeaveHandler(FolderTree, FolderTree_DragLeave);
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var contentGrid = this.FindControl<Grid>("ContentGrid");
        if (contentGrid?.ColumnDefinitions.Count > 0)
            _folderTreeColDef = contentGrid.ColumnDefinitions[0];
        PopulateRootTree();

        if (!string.IsNullOrEmpty(_startupPath))
            await HandleStartupPathAsync(_startupPath);
        else
        {
            var startPath = _vm.Settings.StartupFolder;
            if (!string.IsNullOrEmpty(startPath) && Directory.Exists(startPath))
                NavigateToPath(startPath);
            else if (OperatingSystem.IsWindows())
            {
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (Directory.Exists(pictures))
                    NavigateToPath(pictures);
            }
        }

        RefreshBreadcrumb();
        SyncSortSegments();
        ResetSortButton.IsVisible = _vm.HasCustomFolderSort;

        if (_vm.Settings.CheckForUpdatesOnStartup)
            _ = CheckForUpdatesInBackgroundAsync();

        Gallery.FocusGallery();
    }

    private async Task HandleStartupPathAsync(string path)
    {
        if (Directory.Exists(path))
        {
            NavigateToPath(path);
            return;
        }

        if (File.Exists(path) && SupportedFormats.IsSupported(path))
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is null) return;

            NavigateToPath(folder);

            var match = _vm.SortedImages.FirstOrDefault(
                i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _vm.SelectedIndex = _vm.SortedImages.IndexOf(match);
                _vm.SelectedItem = match;
                _vm.EnterFullscreen();
            }
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var newVersion = await _updateService.CheckForUpdatesAsync();
            if (newVersion is null) return;

            var dialog = new UpdatePromptDialog(newVersion);
            await dialog.ShowDialog(this);

            switch (dialog.Result)
            {
                case UpdatePromptResult.InstallNow:
                    UpdateNotification.Text = "Downloading update...";
                    UpdateNotification.IsVisible = true;
                    var downloaded = await _updateService.DownloadAsync(p =>
                    {
                        Dispatcher.UIThread.Post(() =>
                            UpdateNotification.Text = $"Downloading update... {p}%");
                    });
                    if (downloaded)
                        _updateService.ApplyAndRestart();
                    else
                        UpdateNotification.Text = "Update download failed";
                    break;

                case UpdatePromptResult.InstallOnClose:
                    UpdateNotification.Text = "Downloading update...";
                    UpdateNotification.IsVisible = true;
                    var success = await _updateService.DownloadAsync(p =>
                    {
                        Dispatcher.UIThread.Post(() =>
                            UpdateNotification.Text = $"Downloading update... {p}%");
                    });
                    if (success)
                    {
                        _updateReadyToApply = true;
                        UpdateNotification.Text = "Update ready — will install on close";
                    }
                    else
                        UpdateNotification.Text = "Update download failed";
                    break;

                case UpdatePromptResult.Ignore:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            _vm.StatusText = "Update check failed";
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        _vm.Dispose();
        if (_updateReadyToApply)
            _updateService.ApplyOnExit();
    }

    #region ViewModel PropertyChanged

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsFolderTreeVisible):
                UpdateFolderTreeVisibility();
                break;
            case nameof(MainViewModel.CurrentSortField):
            case nameof(MainViewModel.CurrentSortDirection):
                SyncSortSegments();
                SyncSortDirection();
                break;
            case nameof(MainViewModel.HasCustomFolderSort):
                ResetSortButton.IsVisible = _vm.HasCustomFolderSort;
                break;
            case nameof(MainViewModel.IsLoading):
                LoadingOverlay.IsVisible = _vm.IsLoading;
                break;
            case nameof(MainViewModel.IsDarkTheme):
                UpdateTheme();
                break;
            case nameof(MainViewModel.IsFullscreenActive):
                if (_vm.IsFullscreenActive) ShowFullscreenViewer();
                break;
            case nameof(MainViewModel.CurrentPath):
                AddressBar.Text = _vm.CurrentPath ?? "";
                if (!_addressEditMode)
                    RefreshBreadcrumb();
                break;
        }
    }

    private void UpdateTheme()
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant =
                _vm.IsDarkTheme ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        }
    }

    private void UpdateFolderTreeVisibility()
    {
        if (_folderTreeColDef is null) return;

        if (_vm.IsFolderTreeVisible)
        {
            _folderTreeColDef.Width = new GridLength(_folderTreeWidth, GridUnitType.Pixel);
            FolderTree.IsVisible = true;
        }
        else
        {
            _folderTreeWidth = _folderTreeColDef.Width.Value;
            _folderTreeColDef.Width = new GridLength(0);
            FolderTree.IsVisible = false;
        }
    }

    private void SyncSortSegments()
    {
        foreach (var child in SortSegments.Children)
        {
            if (child is RadioButton rb && rb.Tag is SortField field)
            {
                rb.IsChecked = field == _vm.CurrentSortField;
                if (rb.IsChecked == true)
                {
                    string arrow = _vm.CurrentSortDirection == SortDirection.Ascending ? " \u25B2" : " \u25BC";
                    string baseName = field switch
                    {
                        SortField.FileName => "Name",
                        SortField.DateModified => "Modified",
                        SortField.DateCreated => "Created",
                        SortField.DateTaken => "Taken",
                        SortField.FileSize => "Size",
                        SortField.Dimensions => "Dims",
                        SortField.FileType => "Type",
                        SortField.Rating => "Rating",
                        _ => field.ToString()
                    };
                    rb.Content = baseName + arrow;
                }
                else
                {
                    rb.Content = field switch
                    {
                        SortField.FileName => "Name",
                        SortField.DateModified => "Modified",
                        SortField.DateCreated => "Created",
                        SortField.DateTaken => "Taken",
                        SortField.FileSize => "Size",
                        SortField.Dimensions => "Dims",
                        SortField.FileType => "Type",
                        SortField.Rating => "Rating",
                        _ => field.ToString()
                    };
                }
            }
        }
    }

    private void SyncSortDirection()
    {
        SortDirButton.Content = _vm.CurrentSortDirection == SortDirection.Ascending ? "↑" : "↓";
    }

    #endregion

    #region Navigation

    private void NavigateToPath(string path, string? selectFolderName = null)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        if (!_navigatingFromHistory && !string.IsNullOrEmpty(_vm.CurrentPath))
        {
            _backHistory.Push(_vm.CurrentPath);
            _forwardHistory.Clear();
        }

        _vm.NavigateToFolder(path, selectFolderName);
        SyncFolderTree(path);
        AddressBar.Text = path;
        SetAddressEditMode(false);
        RefreshBreadcrumb();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_backHistory.Count == 0) return;
        if (!string.IsNullOrEmpty(_vm.CurrentPath))
            _forwardHistory.Push(_vm.CurrentPath);

        _navigatingFromHistory = true;
        NavigateToPath(_backHistory.Pop());
        _navigatingFromHistory = false;
    }

    private void ForwardButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_forwardHistory.Count == 0) return;
        if (!string.IsNullOrEmpty(_vm.CurrentPath))
            _backHistory.Push(_vm.CurrentPath);

        _navigatingFromHistory = true;
        NavigateToPath(_forwardHistory.Pop());
        _navigatingFromHistory = false;
    }

    private void UpButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.CurrentPath)) return;
        var parent = Directory.GetParent(_vm.CurrentPath)?.FullName;
        if (parent is not null)
        {
            var folderName = Path.GetFileName(_vm.CurrentPath);
            NavigateToPath(parent, folderName);
        }
    }

    private void AddressBar_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = AddressBar.Text?.Trim();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                NavigateToPath(path);
                Gallery.FocusGallery();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddressBar.Text = _vm.CurrentPath;
            SetAddressEditMode(false);
            RefreshBreadcrumb();
            Gallery.FocusGallery();
            e.Handled = true;
        }
    }

    private void BreadcrumbBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SetAddressEditMode(true);
        AddressBar.Focus();
        AddressBar.SelectAll();
        e.Handled = true;
    }

    private void SetAddressEditMode(bool edit)
    {
        _addressEditMode = edit;
        BreadcrumbBar.IsVisible = !edit;
        AddressBar.IsVisible = edit;
    }

    private void RefreshBreadcrumb()
    {
        BreadcrumbPanel.Children.Clear();
        var path = _vm.CurrentPath;
        if (string.IsNullOrEmpty(path))
            return;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            root = path;

        var parts = new List<(string Full, string Label)>();
        var cur = path;
        while (!string.IsNullOrEmpty(cur))
        {
            var name = ReferenceEquals(cur, root) || cur == root
                ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : Path.GetFileName(cur);
            if (string.IsNullOrEmpty(name))
                name = cur;
            parts.Add((cur, name));
            var parent = Directory.GetParent(cur)?.FullName;
            if (parent is null || string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase))
                break;
            cur = parent;
        }

        parts.Reverse();
        for (int i = 0; i < parts.Count; i++)
        {
            var (full, label) = parts[i];
            if (i > 0)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = " › ",
                    Foreground = Brushes.Gray,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
            }

            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(4, 2),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Tag = full
            };
            btn.Click += (_, _) =>
            {
                if (btn.Tag is string fp && Directory.Exists(fp))
                    NavigateToPath(fp);
            };
            BreadcrumbPanel.Children.Add(btn);
        }
    }

    #endregion

    #region Sort Controls

    private void SortSegment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not SortField field) return;
        _vm.SetSortFieldCommand.Execute(field);
        SyncSortSegments();
    }

    private void ResetSortButton_Click(object? sender, RoutedEventArgs e) =>
        _vm.ResetFolderSortCommand.Execute(null);

    #endregion

    #region Folder Tree

    private void PopulateRootTree()
    {
        FolderTree.Items.Clear();

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    var label = string.IsNullOrEmpty(drive.VolumeLabel)
                        ? $"({drive.Name.TrimEnd('\\', '/')})"
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\', '/')})";
                    var item = CreateTreeItem(drive.Name, label);
                    FolderTree.Items.Add(item);
                }
                catch { }
            }

            void AddSpecial(string display, Environment.SpecialFolder folder)
            {
                try
                {
                    var p = Environment.GetFolderPath(folder);
                    if (Directory.Exists(p))
                        FolderTree.Items.Add(CreateTreeItem(p, display));
                }
                catch { }
            }

            AddSpecial("Pictures", Environment.SpecialFolder.MyPictures);
            AddSpecial("Desktop", Environment.SpecialFolder.Desktop);
            AddSpecial("Documents", Environment.SpecialFolder.MyDocuments);
            try
            {
                var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(dl))
                    FolderTree.Items.Add(CreateTreeItem(dl, "Downloads"));
            }
            catch { }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(home))
            {
                var homeItem = CreateTreeItem(home, Path.GetFileName(home));
                FolderTree.Items.Add(homeItem);
            }

            if (Directory.Exists("/"))
            {
                var rootItem = CreateTreeItem("/", "/");
                FolderTree.Items.Add(rootItem);
            }

            if (OperatingSystem.IsMacOS())
            {
                var volumes = "/Volumes";
                if (Directory.Exists(volumes))
                {
                    try
                    {
                        foreach (var vol in Directory.GetDirectories(volumes))
                        {
                            var volItem = CreateTreeItem(vol, Path.GetFileName(vol));
                            FolderTree.Items.Add(volItem);
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private TreeViewItem CreateTreeItem(string path, string displayName)
    {
        var item = new TreeViewItem
        {
            Header = $"📁 {displayName}",
            Tag = path
        };

        try
        {
            if (Directory.GetDirectories(path).Length > 0)
            {
                item.Items.Add(new TreeViewItem { Header = "Loading..." });
            }
        }
        catch { }

        item.PropertyChanged += (s, e) =>
        {
            if (e.Property != TreeViewItem.IsExpandedProperty) return;
            if (s is not TreeViewItem tvi || !tvi.IsExpanded) return;
            if (tvi.Tag is not string itemPath) return;
            if (tvi.Items.Count == 1 && tvi.Items[0] is TreeViewItem placeholder
                && placeholder.Header?.ToString() == "Loading...")
            {
                tvi.Items.Clear();
                LoadSubfolders(tvi, itemPath);
            }
        };

        return item;
    }

    private void LoadSubfolders(TreeViewItem parent, string path)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('.')) continue;
                    parent.Items.Add(CreateTreeItem(dir, name));
                }
                catch { }
            }
        }
        catch { }
    }

    private void FolderTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressTreeNavigation) return;
        if (FolderTree.SelectedItem is TreeViewItem selected && selected.Tag is string path)
        {
            NavigateToPath(path);
        }
    }

    private void SyncFolderTree(string path)
    {
        _suppressTreeNavigation = true;
        try
        {
            ExpandToPath(path);
        }
        finally
        {
            _suppressTreeNavigation = false;
        }
    }

    private void ExpandToPath(string targetPath)
    {
        var parts = new List<string>();
        var current = targetPath;

        while (!string.IsNullOrEmpty(current))
        {
            parts.Add(current);
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }

        parts.Reverse();

        ItemsControl container = FolderTree;
        foreach (var part in parts)
        {
            var normalizedPart = part.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool found = false;

            for (int i = 0; i < container.Items.Count; i++)
            {
                if (container.Items[i] is not TreeViewItem tvi || tvi.Tag is not string itemPath)
                    continue;

                var normalizedItem = itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(normalizedItem, normalizedPart, StringComparison.OrdinalIgnoreCase))
                    continue;

                tvi.IsExpanded = true;

                if (tvi.Items.Count == 1 && tvi.Items[0] is TreeViewItem placeholder
                    && placeholder.Header?.ToString() == "Loading...")
                {
                    tvi.Items.Clear();
                    LoadSubfolders(tvi, itemPath);
                }

                if (normalizedPart.Equals(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    tvi.IsSelected = true;
                }

                container = tvi;
                found = true;
                break;
            }

            if (!found) break;
        }
    }

    #endregion

    #region Keyboard Shortcuts

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;

        if (alt && e.Key == Key.Left) { BackButton_Click(null, new RoutedEventArgs()); e.Handled = true; }
        else if (alt && e.Key == Key.Right) { ForwardButton_Click(null, new RoutedEventArgs()); e.Handled = true; }
        else if (alt && e.Key == Key.Up) { UpButton_Click(null, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.F) { _vm.ToggleFolderTreeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.P) { PrescanButton_Click(null, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.T) { _vm.ToggleThemeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.L)
        {
            SetAddressEditMode(true);
            AddressBar.Focus();
            AddressBar.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemPlus) { _vm.IncreaseThumbnailSizeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.OemMinus) { _vm.DecreaseThumbnailSizeCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F5) { _vm.RefreshCurrentFolder(); e.Handled = true; }
    }

    #endregion

    #region Fullscreen Viewer

    private void ShowFullscreenViewer()
    {
        if (_vm.SelectedItem is null || _vm.SelectedItem.IsFolder) return;

        var viewer = new FullscreenViewer(_vm);
        viewer.Closed += (_, _) =>
        {
            _vm.ExitFullscreen();
            if (_vm.SelectedItem is not null)
                Gallery.ScrollToItem(_vm.SelectedItem);
        };
        viewer.ShowDialog(this);
    }

    #endregion

    #region Toolbar Buttons

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_vm);
        dialog.ShowDialog(this);
    }

    private void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog(_updateService, () => _updateReadyToApply = true);
        dialog.ShowDialog(this);
    }

    private void PrescanButton_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new PrescanDialog(_vm.Database, _vm.CurrentPath);
        dlg.ShowDialog(this);
    }

    private string? GetTreeTargetPath()
    {
        if (FolderTree.SelectedItem is TreeViewItem tvi && tvi.Tag is string p && Directory.Exists(p))
            return p;
        return string.IsNullOrEmpty(_vm.CurrentPath) ? null : _vm.CurrentPath;
    }

    private void TreeNewFolder_Click(object? sender, RoutedEventArgs e)
    {
        var parent = GetTreeTargetPath();
        if (parent is null) return;
        var newPath = FileOperationService.GetNewFolderPath(parent);
        var name = Path.GetFileName(newPath);
        if (FileOperationService.CreateFolder(parent, name))
        {
            _vm.RequestFolderTreeRefresh();
            NavigateToPath(newPath);
        }
    }

    private async void TreeRename_Click(object? sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is not TreeViewItem tvi || tvi.Tag is not string oldPath)
            return;
        var oldName = Path.GetFileName(oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var newName = await DialogUtil.ShowRenamePromptAsync(this, oldName);
        if (string.IsNullOrEmpty(newName)) return;
        var (ok2, _) = FileOperationService.Rename(oldPath, newName);
        if (ok2)
            _vm.RequestFolderTreeRefresh();
    }

    private void TreeDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is not TreeViewItem tvi || tvi.Tag is not string path)
            return;
        if (!FileOperationService.MoveToRecycleBin(path))
            return;
        _vm.RequestFolderTreeRefresh();
        var parent = Directory.GetParent(path)?.FullName;
        if (parent is not null)
            NavigateToPath(parent);
    }

    private void ClearTreeDropHighlight()
    {
        if (_treeDropHighlight is not null)
        {
            _treeDropHighlight.Background = Brushes.Transparent;
            _treeDropHighlight = null;
        }
    }

    private void FolderTree_DragLeave(object? sender, DragEventArgs e) => ClearTreeDropHighlight();

    private TreeViewItem? FindTreeViewItemUnderPointer(DragEventArgs e)
    {
        var pt = e.GetPosition(FolderTree);
        var hit = FolderTree.InputHitTest(pt);
        for (Visual? v = hit as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v is TreeViewItem tvi && tvi.Tag is string path && Directory.Exists(path))
                return tvi;
        }
        return null;
    }

    private TreeViewItem? GetFallbackTreeItemForDrop() =>
        FolderTree.SelectedItem is TreeViewItem sel && sel.Tag is string p && Directory.Exists(p) ? sel : null;

    private void FolderTree_DragOver(object? sender, DragEventArgs e)
    {
        ClearTreeDropHighlight();

        if (!_vm.Settings.FileOperationsEnabled || !e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var droppedFiles = e.DataTransfer.TryGetFiles();
        var paths = droppedFiles?.Select(f => f.Path.LocalPath)
            .Where(static p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)))
            .ToArray() ?? [];
        if (paths.Length == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var tvi = FindTreeViewItemUnderPointer(e) ?? GetFallbackTreeItemForDrop();
        if (tvi?.Tag is not string targetPath || !Directory.Exists(targetPath))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        bool anySourceIsTarget = paths.Any(f =>
            string.Equals(Path.GetDirectoryName(f), targetPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
        if (anySourceIsTarget)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        bool ctrlHeld = (e.KeyModifiers & KeyModifiers.Control) != 0;
        e.DragEffects = ctrlHeld ? DragDropEffects.Copy : DragDropEffects.Move;

        tvi.Background = new SolidColorBrush(Color.Parse("#330078D7"));
        _treeDropHighlight = tvi;
    }

    private void FolderTree_Drop(object? sender, DragEventArgs e)
    {
        ClearTreeDropHighlight();

        if (!_vm.Settings.FileOperationsEnabled || !e.DataTransfer.Contains(DataFormat.File)) return;

        var droppedFiles = e.DataTransfer.TryGetFiles();
        var paths = droppedFiles?.Select(f => f.Path.LocalPath)
            .Where(static p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)))
            .ToArray() ?? [];
        if (paths.Length == 0) return;

        var tvi = FindTreeViewItemUnderPointer(e) ?? GetFallbackTreeItemForDrop();
        string? targetPath = null;
        if (tvi?.Tag is string tp && Directory.Exists(tp))
            targetPath = tp;
        else
            targetPath = GetTreeTargetPath();

        if (targetPath is null) return;

        bool anySourceIsTarget = paths.Any(f =>
            string.Equals(Path.GetDirectoryName(f), targetPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase));
        if (anySourceIsTarget) return;

        bool ctrlHeld = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var internalItems = GalleryInternalDragState.Items;
        var pathSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        bool success = ctrlHeld
            ? FileOperationService.CopyItems(paths, targetPath, default)
            : FileOperationService.MoveItems(paths, targetPath, default);

        if (success && !ctrlHeld && internalItems is { Count: > 0 })
        {
            var moved = internalItems.Where(i => pathSet.Contains(i.FilePath)).ToList();
            if (moved.Count > 0)
                _vm.OnItemsMoved(moved);
            else
                _vm.RefreshCurrentFolder();
        }
        else if (success)
            _vm.RefreshCurrentFolder();
    }

    #endregion
}
