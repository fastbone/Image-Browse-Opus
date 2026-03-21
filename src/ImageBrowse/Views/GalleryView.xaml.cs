using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class GalleryView : UserControl
{
    public event Action<int>? SelectionCountChanged;

    public GalleryView()
    {
        InitializeComponent();
        GalleryListBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        GalleryListBox.SelectionChanged += (_, _) =>
        {
            int count = GalleryListBox.SelectedItems.Count;
            SelectionCountChanged?.Invoke(count);
        };
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is not null)
            {
                ViewModel.SortedImages.CollectionChanged += (_, _) => UpdateEmptyState();
                ViewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsLoading))
                        UpdateEmptyState();
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

    private void GalleryListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.F:
                if (ViewModel.SelectedItem?.IsFolder == true)
                    _ = ViewModel.NavigateToFolder(ViewModel.SelectedItem.FilePath);
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

            case Key.Home:
                ViewModel.GetFirstImage();
                e.Handled = true;
                break;

            case Key.End:
                ViewModel.GetLastImage();
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
            _ = ViewModel.NavigateToFolder(ViewModel.SelectedItem.FilePath);
        else
            ViewModel.EnterFullscreen();
    }

    private async void RotateSelectedImages()
    {
        if (ViewModel is null) return;
        var items = GalleryListBox.SelectedItems.Cast<Models.ImageItem>()
            .Where(i => !i.IsFolder).ToList();
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
        var parent = System.IO.Directory.GetParent(ViewModel.CurrentPath);
        if (parent is not null)
            _ = ViewModel.NavigateToFolder(parent.FullName);
    }
}
