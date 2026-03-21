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
                SortDirectionIcon.Text = _vm.CurrentSortDirection == SortDirection.Ascending ? "\uE74A" : "\uE74B";
                break;
            case nameof(MainViewModel.CurrentSortField):
                SyncSortComboBox();
                break;
            case nameof(MainViewModel.IsLoading):
                HandleLoadingChanged(_vm.IsLoading);
                break;
            case nameof(MainViewModel.IsFolderTreeVisible):
                AnimateFolderTree(_vm.IsFolderTreeVisible);
                break;
        }
    }

    private void HandleLoadingChanged(bool isLoading)
    {
        if (isLoading)
        {
            _loadingDelayTimer?.Stop();
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
                var fadeOut = new DoubleAnimation(LoadingOverlay.Opacity, 0, TimeSpan.FromMilliseconds(150));
                fadeOut.Completed += (_, _) => LoadingOverlay.Visibility = Visibility.Collapsed;
                LoadingOverlay.BeginAnimation(OpacityProperty, fadeOut);
            }
        }
    }

    private void ShowEscapeToast()
    {
        _escapeToastTimer?.Stop();
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        EscapeToast.BeginAnimation(OpacityProperty, fadeIn);

        _escapeToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _escapeToastTimer.Tick += (_, _) =>
        {
            _escapeToastTimer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            EscapeToast.BeginAnimation(OpacityProperty, fadeOut);
        };
        _escapeToastTimer.Start();
    }

    private void AnimateFolderTree(bool show)
    {
        double from = FolderTreeColumn.Width.Value;
        double to = show ? _folderTreeWidth : 0;

        if (!show && from > 0)
            _folderTreeWidth = from;

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

    private void SyncSortComboBox()
    {
        foreach (ComboBoxItem item in SortFieldCombo.Items)
        {
            if (item.Tag is SortField field && field == _vm.CurrentSortField)
            {
                SortFieldCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void ApplyTheme(bool isDark)
    {
        var themeUri = isDark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var theme = new ResourceDictionary { Source = themeUri };

        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(theme);

        Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush");
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

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeNavigation) return;
        if (e.NewValue is TreeViewItem item && item.Tag is string path)
        {
            _ = NavigateToPath(path);
        }
    }

    private async Task NavigateToPath(string path)
    {
        if (!Directory.Exists(path)) return;

        if (!_navigatingFromHistory && !string.IsNullOrEmpty(_vm.CurrentPath) && _vm.CurrentPath != path)
        {
            _backHistory.Push(_vm.CurrentPath);
            if (!_navigatingFromHistory) _forwardHistory.Clear();
        }

        await _vm.NavigateToFolder(path);
        SyncFolderTree(path);
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var path = AddressBar.Text.Trim();
            if (Directory.Exists(path))
                _ = NavigateToPath(path);
            e.Handled = true;
        }
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
        var parent = Directory.GetParent(_vm.CurrentPath);
        if (parent is not null)
            _ = NavigateToPath(parent.FullName);
    }

    private void SortFieldCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is not null && SortFieldCombo.SelectedItem is ComboBoxItem item && item.Tag is SortField field)
        {
            _vm.CurrentSortField = field;
        }
    }

    private void OpenFullscreenViewer()
    {
        var viewer = new FullscreenViewer(_vm);
        viewer.Closed += (_, _) => _vm.ExitFullscreen();
        viewer.ShowDialog();
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

    private void OpenSettingsDialog()
    {
        var dialog = new SettingsDialog(_vm);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            ApplyTheme(_vm.IsDarkTheme);
            SyncSortComboBox();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog();
    }

    private void ResetSortButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetFolderSortCommand.Execute(null);
        SyncSortComboBox();
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

        var dialog = new PrescanDialog(_vm.Database, currentPath);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAboutDialog();
    }

    private void OpenAboutDialog()
    {
        var dialog = new AboutDialog(_updateService);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void UpdateNotification_MouseDown(object sender, MouseButtonEventArgs e)
    {
        OpenAboutDialog();
    }
}
