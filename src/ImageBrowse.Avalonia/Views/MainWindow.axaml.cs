using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageBrowse.Models;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = null!;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private bool _navigatingFromHistory;
    private bool _suppressTreeNavigation;
    private double _folderTreeWidth = 240;
    private ColumnDefinition? _folderTreeColDef;

    public MainWindow() : this(null!) { }

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
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
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        var contentGrid = this.FindControl<Grid>("ContentGrid");
        if (contentGrid?.ColumnDefinitions.Count > 0)
            _folderTreeColDef = contentGrid.ColumnDefinitions[0];
        PopulateRootTree();

        var startPath = _vm.Settings.StartupFolder;
        if (!string.IsNullOrEmpty(startPath) && Directory.Exists(startPath))
        {
            NavigateToPath(startPath);
        }
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
                SyncSortCombo();
                break;
            case nameof(MainViewModel.CurrentSortDirection):
                SyncSortDirection();
                break;
            case nameof(MainViewModel.IsLoading):
                LoadingOverlay.IsVisible = _vm.IsLoading;
                break;
            case nameof(MainViewModel.IsDarkTheme):
                UpdateTheme();
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

    private void SyncSortCombo()
    {
        for (int i = 0; i < SortFieldCombo.Items.Count; i++)
        {
            if (SortFieldCombo.Items[i] is ComboBoxItem ci &&
                ci.Tag is SortField field && field == _vm.CurrentSortField)
            {
                SortFieldCombo.SelectedIndex = i;
                break;
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
            Gallery.FocusGallery();
            e.Handled = true;
        }
    }

    #endregion

    #region Sort Controls

    private void SortFieldCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SortFieldCombo.SelectedItem is ComboBoxItem ci && ci.Tag is SortField field)
        {
            _vm.SetSortFieldCommand.Execute(field);
        }
    }

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
        else if (ctrl && e.Key == Key.T) { _vm.ToggleThemeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.L) { AddressBar.Focus(); AddressBar.SelectAll(); e.Handled = true; }
        else if (ctrl && e.Key == Key.OemPlus) { _vm.IncreaseThumbnailSizeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.OemMinus) { _vm.DecreaseThumbnailSizeCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F5) { _vm.RefreshCurrentFolder(); e.Handled = true; }
    }

    #endregion

    #region Toolbar Buttons

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        // Settings dialog - placeholder for now
    }

    #endregion
}
