using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class GalleryView : UserControl
{
    public event Action<int>? SelectionCountChanged;

    private static readonly DoubleAnimation FadeInAnim = new(0, 1, TimeSpan.FromMilliseconds(180))
    {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };

    public GalleryView()
    {
        InitializeComponent();
        GalleryListBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        GalleryListBox.SelectionChanged += (_, _) =>
        {
            int count = GalleryListBox.SelectedItems.Count;
            SelectionCountChanged?.Invoke(count);
        };
        GalleryListBox.ItemContainerGenerator.StatusChanged += OnContainerStatusChanged;
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is not null)
            {
                ViewModel.SortedImages.CollectionChanged += (_, _) => UpdateEmptyState();
                ViewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsLoading))
                        UpdateEmptyState();
                    else if (e.PropertyName == nameof(MainViewModel.SelectedIndex))
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, FocusSelectedItem);
                };
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void UpdateEmptyState()
    {
        if (ViewModel is null) return;
        bool isEmpty = ViewModel.SortedImages.Count == 0 && !ViewModel.IsLoading
                       && !string.IsNullOrEmpty(ViewModel.CurrentPath);
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnContainerStatusChanged(object? sender, EventArgs e)
    {
        if (GalleryListBox.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            return;
        if (ViewModel is null || !ViewModel.Settings.EnableAnimations) return;

        for (int i = 0; i < GalleryListBox.Items.Count; i++)
        {
            if (GalleryListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                continue;
            if (container.Tag is "revealed") continue;
            container.Tag = "revealed";
            container.Opacity = 0;
            container.BeginAnimation(OpacityProperty, FadeInAnim);
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ViewModel is null) return;
        var scrollViewer = FindScrollViewer(GalleryListBox);
        if (scrollViewer is null) return;

        int firstVisible = 0;
        int lastVisible = 0;

        for (int i = 0; i < ViewModel.SortedImages.Count; i++)
        {
            var container = GalleryListBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            var transform = container.TransformToAncestor(scrollViewer);
            var point = transform.Transform(new Point(0, 0));

            if (point.Y + container.ActualHeight >= 0 && point.Y <= scrollViewer.ViewportHeight)
            {
                if (firstVisible == 0 && i > 0) firstVisible = i;
                lastVisible = i;
            }
            else if (lastVisible > 0)
            {
                break;
            }
        }

        ViewModel.RequestThumbnailsForVisibleRange(firstVisible, lastVisible);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject dep)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = VisualTreeHelper.GetChild(dep, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
    }

    public void FocusGallery()
    {
        Keyboard.Focus(GalleryListBox);
        FocusSelectedItem();
    }

    private void FocusSelectedItem()
    {
        if (ViewModel is null || ViewModel.SelectedIndex < 0) return;
        if (ViewModel.SelectedIndex < ViewModel.SortedImages.Count)
        {
            GalleryListBox.ScrollIntoView(ViewModel.SortedImages[ViewModel.SelectedIndex]);
            GalleryListBox.UpdateLayout();
        }
        if (GalleryListBox.ItemContainerGenerator.ContainerFromIndex(ViewModel.SelectedIndex)
            is ListBoxItem container)
        {
            container.Focus();
        }
    }

    private void GalleryListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null || ViewModel.SortedImages.Count == 0) return;

        switch (e.Key)
        {
            case Key.Home:
            {
                ViewModel.SelectedIndex = 0;
                ViewModel.SelectedItem = ViewModel.SortedImages[0];
                e.Handled = true;
                break;
            }
            case Key.End:
            {
                int last = ViewModel.SortedImages.Count - 1;
                ViewModel.SelectedIndex = last;
                ViewModel.SelectedItem = ViewModel.SortedImages[last];
                e.Handled = true;
                break;
            }
            case Key.PageDown:
            {
                int pageSize = EstimatePageSize();
                int current = Math.Max(0, ViewModel.SelectedIndex);
                int target = Math.Min(current + pageSize, ViewModel.SortedImages.Count - 1);
                ViewModel.SelectedIndex = target;
                ViewModel.SelectedItem = ViewModel.SortedImages[target];
                e.Handled = true;
                break;
            }
            case Key.PageUp:
            {
                int pageSize = EstimatePageSize();
                int current = ViewModel.SelectedIndex < 0
                    ? ViewModel.SortedImages.Count - 1
                    : ViewModel.SelectedIndex;
                int target = Math.Max(current - pageSize, 0);
                ViewModel.SelectedIndex = target;
                ViewModel.SelectedItem = ViewModel.SortedImages[target];
                e.Handled = true;
                break;
            }
        }
    }

    private int EstimatePageSize()
    {
        var scrollViewer = FindScrollViewer(GalleryListBox);
        if (scrollViewer is null || ViewModel is null) return 10;

        double itemSize = ViewModel.ThumbnailSize + 24;
        if (itemSize <= 0) return 10;

        int cols = Math.Max(1, (int)(scrollViewer.ViewportWidth / itemSize));
        int rows = Math.Max(1, (int)(scrollViewer.ViewportHeight / itemSize));
        return cols * rows;
    }

    private void GalleryListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.F:
                if (ViewModel.SelectedItem?.IsFolder == true)
                {
                    string? returnTo = ViewModel.SelectedItem.IsParentFolder
                        ? Path.GetFileName(ViewModel.CurrentPath)
                        : null;
                    _ = ViewModel.NavigateToFolder(ViewModel.SelectedItem.FilePath, returnTo);
                }
                else
                    ViewModel.EnterFullscreen();
                e.Handled = true;
                break;

            case Key.Back:
            case Key.BrowserBack:
                NavigateUp();
                e.Handled = true;
                break;

            case Key.D1 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad1 when Keyboard.Modifiers == ModifierKeys.None:
                ViewModel.SetRatingCommand.Execute("1");
                e.Handled = true;
                break;
            case Key.D2 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad2 when Keyboard.Modifiers == ModifierKeys.None:
                ViewModel.SetRatingCommand.Execute("2");
                e.Handled = true;
                break;
            case Key.D3 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad3 when Keyboard.Modifiers == ModifierKeys.None:
                ViewModel.SetRatingCommand.Execute("3");
                e.Handled = true;
                break;
            case Key.D4 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad4 when Keyboard.Modifiers == ModifierKeys.None:
                ViewModel.SetRatingCommand.Execute("4");
                e.Handled = true;
                break;
            case Key.D5 when Keyboard.Modifiers == ModifierKeys.None:
            case Key.NumPad5 when Keyboard.Modifiers == ModifierKeys.None:
                ViewModel.SetRatingCommand.Execute("5");
                e.Handled = true;
                break;

            case Key.R when Keyboard.Modifiers == ModifierKeys.None:
                RotateSelectedImages();
                e.Handled = true;
                break;

            case Key.Q:
                ViewModel.ToggleTagCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                DeleteSelectedImages();
                e.Handled = true;
                break;
        }
    }

    private void GalleryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.SelectedItem is null) return;

        if (ViewModel.SelectedItem.IsFolder)
        {
            string? returnTo = ViewModel.SelectedItem.IsParentFolder
                ? Path.GetFileName(ViewModel.CurrentPath)
                : null;
            _ = ViewModel.NavigateToFolder(ViewModel.SelectedItem.FilePath, returnTo);
        }
        else
            ViewModel.EnterFullscreen();
    }

    private async void RotateSelectedImages()
    {
        if (ViewModel is null) return;
        var items = GalleryListBox.SelectedItems.Cast<Models.ImageItem>()
            .Where(i => !i.IsFolder && !i.IsVideo).ToList();
        if (items.Count == 0) return;

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

        if (failed > 0)
            MessageBox.Show(Window.GetWindow(this),
                $"Failed to rotate {failed} file{(failed != 1 ? "s" : "")}. The file may be read-only or in use.",
                "Rotation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DeleteSelectedImages()
    {
        if (ViewModel is null) return;
        var items = GalleryListBox.SelectedItems.Cast<Models.ImageItem>()
            .Where(i => !i.IsFolder).ToList();
        if (items.Count == 0) return;

        if (ViewModel.Settings.ConfirmBeforeDelete)
        {
            string msg = items.Count == 1
                ? $"Move \"{items[0].FileName}\" to the Recycle Bin?"
                : $"Move {items.Count} files to the Recycle Bin?";

            var result = MessageBox.Show(Window.GetWindow(this),
                msg + "\n\nYou can disable this confirmation in Settings.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        int currentIdx = ViewModel.SelectedIndex;
        var deleted = new List<Models.ImageItem>();

        foreach (var item in items)
        {
            if (FileOperationService.MoveToRecycleBin(item.FilePath))
                deleted.Add(item);
        }

        if (deleted.Count > 0)
        {
            ViewModel.RemoveImages(deleted);

            if (ViewModel.SortedImages.Count > 0)
            {
                int newIdx = Math.Min(currentIdx, ViewModel.SortedImages.Count - 1);
                ViewModel.SelectedIndex = newIdx;
                ViewModel.SelectedItem = ViewModel.SortedImages[newIdx];
            }
        }
    }

    private void NavigateUp()
    {
        if (ViewModel is null || string.IsNullOrEmpty(ViewModel.CurrentPath)) return;
        var folderName = Path.GetFileName(ViewModel.CurrentPath);
        var parent = System.IO.Directory.GetParent(ViewModel.CurrentPath);
        if (parent is not null)
            _ = ViewModel.NavigateToFolder(parent.FullName, folderName);
    }
}
