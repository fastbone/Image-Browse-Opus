using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;
using ImageBrowse.Views;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ImageBrowse;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly UpdateService _updateService = new();
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private bool _navigatingFromHistory;
    private bool _suppressTreeNavigation;
    private DateTime _lastEscapePress = DateTime.MinValue;
    private DispatcherTimer? _escapeToastTimer;
    private DispatcherTimer? _loadingDelayTimer;
    private readonly string? _startupPath;
    private double _folderTreeWidth = 240;
    private const double FolderTreeAnimDuration = 200;
    private const double FolderTreeMinWidth = 120;

    public MainWindow(string? startupPath = null)
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _startupPath = startupPath;

        _vm.PropertyChanged += ViewModel_PropertyChanged;
        Gallery.SelectionCountChanged += count =>
        {
            SelectionCountText.Text = count > 1 ? $"{count} selected" : "";
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(_vm.IsDarkTheme);
        PopulateDriveTree();

        if (!string.IsNullOrEmpty(_startupPath))
        {
            _ = HandleStartupPath(_startupPath);
        }
        else
        {
            var startPath = _vm.Settings.StartupFolder;
            if (!Directory.Exists(startPath))
                startPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(startPath))
                _ = NavigateToPath(startPath);
        }

        _ = CheckForUpdatesInBackground();
        FocusGallery();
    }

    private async Task HandleStartupPath(string path)
    {
        if (Directory.Exists(path))
        {
            await NavigateToPath(path);
            return;
        }

        if (File.Exists(path) && ImageLoadingService.IsSupported(path))
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is null) return;

            await NavigateToPath(folder);

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

    private async Task CheckForUpdatesInBackground()
    {
        try
        {
            var newVersion = await _updateService.CheckForUpdatesAsync();
            if (newVersion is not null)
            {
                UpdateNotification.Text = $"Update {newVersion} available — click to update";
                UpdateNotification.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            _vm.StatusText = "Update check failed";
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _vm.Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsDarkTheme):
                ApplyTheme(_vm.IsDarkTheme);
                break;
            case nameof(MainViewModel.IsFullscreenActive) when _vm.IsFullscreenActive:
                OpenFullscreenViewer();
                break;
            case nameof(MainViewModel.CurrentSortDirection):
                SyncSortSegments();
                break;
            case nameof(MainViewModel.CurrentSortField):
                SyncSortSegments();
                break;
            case nameof(MainViewModel.IsLoading):
                HandleLoadingChanged(_vm.IsLoading);
                break;
            case nameof(MainViewModel.IsFolderTreeVisible):
                AnimateFolderTree(_vm.IsFolderTreeVisible);
                break;
            case nameof(MainViewModel.CurrentPath):
                UpdateBreadcrumbs();
                break;
        }
    }

    private void HandleLoadingChanged(bool isLoading)
    {
        bool animate = _vm.Settings.EnableAnimations;

        if (isLoading)
        {
            _loadingDelayTimer?.Stop();
            if (!animate)
            {
                LoadingOverlay.Opacity = 1;
                LoadingOverlay.Visibility = Visibility.Visible;
                return;
            }
            _loadingDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _loadingDelayTimer.Tick += (_, _) =>
            {
                _loadingDelayTimer.Stop();
                if (_vm.IsLoading)
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                    LoadingOverlay.BeginAnimation(OpacityProperty, fadeIn);
                }
            };
            _loadingDelayTimer.Start();
        }
        else
        {
            _loadingDelayTimer?.Stop();
            if (LoadingOverlay.Visibility == Visibility.Visible)
            {
                if (!animate)
                {
                    LoadingOverlay.BeginAnimation(OpacityProperty, null);
                    LoadingOverlay.Opacity = 0;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }
                var fadeOut = new DoubleAnimation(LoadingOverlay.Opacity, 0, TimeSpan.FromMilliseconds(150));
                fadeOut.Completed += (_, _) => LoadingOverlay.Visibility = Visibility.Collapsed;
                LoadingOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
        }
    }

    private void ShowEscapeToast()
    {
        _escapeToastTimer?.Stop();
        bool animate = _vm.Settings.EnableAnimations;

        if (animate)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            EscapeToast.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            EscapeToast.BeginAnimation(OpacityProperty, null);
            EscapeToast.Opacity = 1;
        }

        _escapeToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _escapeToastTimer.Tick += (_, _) =>
        {
            _escapeToastTimer.Stop();
            if (animate)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                EscapeToast.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                EscapeToast.BeginAnimation(OpacityProperty, null);
                EscapeToast.Opacity = 0;
            }
        };
        _escapeToastTimer.Start();
    }

    private void AnimateFolderTree(bool show)
    {
        double from = FolderTreeColumn.Width.Value;
        double to = show ? _folderTreeWidth : 0;

        if (!show && from > 0)
            _folderTreeWidth = from;

        if (!_vm.Settings.EnableAnimations)
        {
            FolderTreeColumn.Width = new GridLength(to);
            FolderTree.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            FolderTreeSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            FolderTreeColumn.MinWidth = show ? FolderTreeMinWidth : 0;
            return;
        }

        if (show)
        {
            FolderTree.Visibility = Visibility.Visible;
            FolderTreeSplitter.Visibility = Visibility.Visible;
            FolderTreeColumn.MinWidth = 0;
        }

        int steps = 12;
        int stepMs = (int)(FolderTreeAnimDuration / steps);
        int currentStep = 0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
        timer.Tick += (_, _) =>
        {
            currentStep++;
            double t = (double)currentStep / steps;
            t = 1 - Math.Pow(1 - t, 3);
            double width = from + (to - from) * t;
            FolderTreeColumn.Width = new GridLength(Math.Max(0, width));

            if (currentStep >= steps)
            {
                timer.Stop();
                FolderTreeColumn.Width = new GridLength(to);
                if (!show)
                {
                    FolderTree.Visibility = Visibility.Collapsed;
                    FolderTreeSplitter.Visibility = Visibility.Collapsed;
                }
                else
                {
                    FolderTreeColumn.MinWidth = FolderTreeMinWidth;
                }
            }
        };
        timer.Start();
    }

    private void SyncSortSegments()
    {
        foreach (var child in SortSegments.Children)
        {
            if (child is System.Windows.Controls.RadioButton rb && rb.Tag is SortField field)
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

    private void ApplyTheme(bool isDark)
    {
        bool animate = _vm.Settings.EnableAnimations && IsLoaded && ActualWidth > 0 && ActualHeight > 0;
        System.Windows.Media.Imaging.RenderTargetBitmap? snapshot = null;

        if (animate)
        {
            try
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)(ActualWidth * dpi.DpiScaleX),
                    (int)(ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                    System.Windows.Media.PixelFormats.Pbgra32);
                snapshot.Render(this);
                snapshot.Freeze();
            }
            catch
            {
                snapshot = null;
            }
        }

        var themeUri = isDark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var theme = new ResourceDictionary { Source = themeUri };

        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(theme);

        Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush");

        if (snapshot is not null)
        {
            ThemeCrossfadeOverlay.Source = snapshot;
            ThemeCrossfadeOverlay.Opacity = 1;
            ThemeCrossfadeOverlay.Visibility = Visibility.Visible;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (_, _) =>
            {
                ThemeCrossfadeOverlay.Visibility = Visibility.Collapsed;
                ThemeCrossfadeOverlay.Source = null;
            };
            ThemeCrossfadeOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    private void PopulateDriveTree()
    {
        FolderTree.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var item = CreateTreeItem(drive.RootDirectory);
            FolderTree.Items.Add(item);
        }

        AddSpecialFolder("Pictures", Environment.SpecialFolder.MyPictures);
        AddSpecialFolder("Desktop", Environment.SpecialFolder.Desktop);
        AddSpecialFolder("Documents", Environment.SpecialFolder.MyDocuments);
        AddSpecialFolder("Downloads", GetDownloadsPath());
    }

    private void AddSpecialFolder(string label, Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        if (Directory.Exists(path))
        {
            var item = CreateTreeItem(new DirectoryInfo(path), label);
            FolderTree.Items.Insert(0, item);
        }
    }

    private void AddSpecialFolder(string label, string? path)
    {
        if (path is not null && Directory.Exists(path))
        {
            var item = CreateTreeItem(new DirectoryInfo(path), label);
            FolderTree.Items.Insert(0, item);
        }
    }

    private static string? GetDownloadsPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private TreeViewItem CreateTreeItem(DirectoryInfo dir, string? displayName = null)
    {
        var item = new TreeViewItem
        {
            Header = displayName ?? dir.Name,
            Tag = dir.FullName,
            FontSize = 13
        };

        // Lazy load placeholder
        item.Items.Add(new TreeViewItem { Header = "Loading..." });
        item.Expanded += TreeItem_Expanded;

        return item;
    }

    private void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || item.Tag is not string path) return;

        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem placeholder && placeholder.Header is "Loading...")
        {
            item.Items.Clear();
            try
            {
                var dirs = Directory.GetDirectories(path)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d.Name, Helpers.NaturalSortComparer.Instance);

                foreach (var dir in dirs)
                {
                    item.Items.Add(CreateTreeItem(dir));
                }
            }
            catch (UnauthorizedAccessException)
            {
                item.Items.Add(new TreeViewItem
                {
                    Header = "(Access denied)",
                    IsEnabled = false,
                    FontStyle = System.Windows.FontStyles.Italic
                });
            }
            catch
            {
                item.Items.Add(new TreeViewItem
                {
                    Header = "(Error loading)",
                    IsEnabled = false,
                    FontStyle = System.Windows.FontStyles.Italic
                });
            }
        }
    }

    private void FocusGallery()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Gallery.FocusGallery);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeNavigation) return;
        if (e.NewValue is TreeViewItem item && item.Tag is string path)
        {
            _ = NavigateToPath(path);
        }
    }

    private async Task NavigateToPath(string path, string? selectFolderName = null)
    {
        if (!Directory.Exists(path)) return;

        if (!_navigatingFromHistory && !string.IsNullOrEmpty(_vm.CurrentPath) && _vm.CurrentPath != path)
        {
            _backHistory.Push(_vm.CurrentPath);
            if (!_navigatingFromHistory) _forwardHistory.Clear();
        }

        await _vm.NavigateToFolder(path, selectFolderName);
        SyncFolderTree(path);
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = AddressBar.Text.Trim();
            if (Directory.Exists(path))
                _ = NavigateToPath(path);
            ExitAddressBarEditMode();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddressBar.Text = _vm.CurrentPath;
            ExitAddressBarEditMode();
            e.Handled = true;
        }
    }

    private void AddressBar_LostFocus(object sender, RoutedEventArgs e)
    {
        ExitAddressBarEditMode();
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backHistory.Count == 0) return;
        _forwardHistory.Push(_vm.CurrentPath);
        _navigatingFromHistory = true;
        try
        {
            await NavigateToPath(_backHistory.Pop());
        }
        finally
        {
            _navigatingFromHistory = false;
        }
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forwardHistory.Count == 0) return;
        _backHistory.Push(_vm.CurrentPath);
        _navigatingFromHistory = true;
        try
        {
            await NavigateToPath(_forwardHistory.Pop());
        }
        finally
        {
            _navigatingFromHistory = false;
        }
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        var folderName = Path.GetFileName(_vm.CurrentPath);
        var parent = Directory.GetParent(_vm.CurrentPath);
        if (parent is not null)
            _ = NavigateToPath(parent.FullName, folderName);
    }

    private void SortSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton rb || rb.Tag is not SortField field) return;

        if (_vm.CurrentSortField == field)
        {
            _vm.ToggleSortDirectionCommand.Execute(null);
        }
        else
        {
            _vm.CurrentSortField = field;
        }
        SyncSortSegments();
    }

    private void OpenFullscreenViewer()
    {
        var viewer = new FullscreenViewer(_vm);
        viewer.Owner = this;
        viewer.Closed += (_, _) => _vm.ExitFullscreen();
        viewer.ShowDialog();
        FocusGallery();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastEscapePress).TotalMilliseconds < 2000)
            {
                Application.Current.Shutdown();
            }
            else
            {
                _lastEscapePress = now;
                ShowEscapeToast();
            }
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is TextBox) return;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.F11:
                _vm.EnterFullscreen();
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.None:
                _vm.EnterFullscreen();
                e.Handled = true;
                break;

            case Key.T when Keyboard.Modifiers == ModifierKeys.Control:
                _vm.ToggleThemeCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                _vm.ToggleFolderTreeCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.Add when Keyboard.Modifiers == ModifierKeys.Control:
                _vm.IncreaseThumbnailSizeCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.OemMinus when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.Subtract when Keyboard.Modifiers == ModifierKeys.Control:
                _vm.DecreaseThumbnailSizeCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Left when Keyboard.Modifiers == ModifierKeys.Alt:
                BackButton_Click(sender, e);
                e.Handled = true;
                break;

            case Key.Right when Keyboard.Modifiers == ModifierKeys.Alt:
                ForwardButton_Click(sender, e);
                e.Handled = true;
                break;

            case Key.Up when Keyboard.Modifiers == ModifierKeys.Alt:
                UpButton_Click(sender, e);
                e.Handled = true;
                break;

            case Key.OemComma when Keyboard.Modifiers == ModifierKeys.Control:
                OpenSettingsDialog();
                e.Handled = true;
                break;

            case Key.P when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                OpenPrescanDialog();
                e.Handled = true;
                break;

            case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                EnterAddressBarEditMode();
                e.Handled = true;
                break;

            case Key.F1:
                OpenAboutDialog();
                e.Handled = true;
                break;
        }
    }

    private void SyncFolderTree(string targetPath)
    {
        _suppressTreeNavigation = true;
        try
        {
            targetPath = Path.GetFullPath(targetPath);

            foreach (TreeViewItem root in FolderTree.Items)
            {
                if (root.Tag is not string rootPath) continue;

                string normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedTarget = targetPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!normalizedTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                    && !targetPath.Equals(rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    continue;

                var current = root;
                var remaining = targetPath[rootPath.TrimEnd(Path.DirectorySeparatorChar).Length..].Split(
                    Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

                var itemsToExpand = new List<TreeViewItem>();

                bool matched = true;
                foreach (var segment in remaining)
                {
                    current.IsExpanded = true;
                    itemsToExpand.Add(current);

                    TreeViewItem? child = null;
                    foreach (TreeViewItem c in current.Items)
                    {
                        if (c.Tag is string cp &&
                            Path.GetFileName(cp.TrimEnd(Path.DirectorySeparatorChar))
                                .Equals(segment, StringComparison.OrdinalIgnoreCase))
                        {
                            child = c;
                            break;
                        }
                    }
                    if (child is null) { matched = false; break; }
                    current = child;
                }

                if (itemsToExpand.Count > 0)
                    FolderTree.UpdateLayout();

                if (matched)
                {
                    current.IsSelected = true;
                    current.BringIntoView();
                }
                break;
            }
        }
        catch { }
        finally
        {
            _suppressTreeNavigation = false;
        }
    }

    private void UpdateBreadcrumbs()
    {
        BreadcrumbItems.Items.Clear();
        var path = _vm.CurrentPath;
        if (string.IsNullOrEmpty(path)) return;

        var segments = new List<(string Display, string FullPath)>();
        var current = path;

        while (!string.IsNullOrEmpty(current))
        {
            var dirInfo = new DirectoryInfo(current);
            string display = dirInfo.Parent is null ? current : dirInfo.Name;
            segments.Insert(0, (display, current));
            current = dirInfo.Parent?.FullName;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                var chevron = new System.Windows.Controls.TextBlock
                {
                    Text = "\uE76C",
                    FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
                    FontSize = 9,
                    Foreground = (System.Windows.Media.Brush)FindResource("FgMutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                BreadcrumbItems.Items.Add(chevron);
            }

            var seg = segments[i];
            var btn = new Button
            {
                Content = seg.Display,
                Tag = seg.FullPath,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = (System.Windows.Media.Brush)FindResource("FgSecondaryBrush"),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            btn.Click += BreadcrumbSegment_Click;
            btn.MouseEnter += (s, _) =>
            {
                if (s is Button b) b.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            };
            btn.MouseLeave += (s, _) =>
            {
                if (s is Button b) b.Foreground = (System.Windows.Media.Brush)FindResource("FgSecondaryBrush");
            };
            BreadcrumbItems.Items.Add(btn);
        }
    }

    private void BreadcrumbSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string segPath)
            _ = NavigateToPath(segPath);
    }

    private void BreadcrumbBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBlock tb && tb.Parent is Button)
            return;
        EnterAddressBarEditMode();
    }

    private void EnterAddressBarEditMode()
    {
        BreadcrumbBar.Visibility = Visibility.Collapsed;
        AddressBar.Visibility = Visibility.Visible;
        AddressBar.Text = _vm.CurrentPath;
        AddressBar.Focus();
        AddressBar.SelectAll();
    }

    private void ExitAddressBarEditMode()
    {
        AddressBar.Visibility = Visibility.Collapsed;
        BreadcrumbBar.Visibility = Visibility.Visible;
        FocusGallery();
    }

    private void OpenSettingsDialog()
    {
        var dialog = new SettingsDialog(_vm);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            ApplyTheme(_vm.IsDarkTheme);
            SyncSortSegments();
        }
        FocusGallery();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog();
    }

    private void ResetSortButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetFolderSortCommand.Execute(null);
        SyncSortSegments();
    }

    private void PrescanButton_Click(object sender, RoutedEventArgs e)
    {
        OpenPrescanDialog();
    }

    private void OpenPrescanDialog()
    {
        var currentPath = _vm.CurrentPath;
        if (string.IsNullOrEmpty(currentPath))
            currentPath = _vm.Settings.StartupFolder;

        var dialog = new PrescanDialog(_vm.Database, currentPath, _vm.Settings.EnableAnimations);
        dialog.Owner = this;
        dialog.ShowDialog();
        FocusGallery();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAboutDialog();
    }

    private void OpenAboutDialog()
    {
        var dialog = new AboutDialog(_updateService, _vm.Settings.EnableAnimations);
        dialog.Owner = this;
        dialog.ShowDialog();
        FocusGallery();
    }

    private void UpdateNotification_MouseDown(object sender, MouseButtonEventArgs e)
    {
        OpenAboutDialog();
    }
}
