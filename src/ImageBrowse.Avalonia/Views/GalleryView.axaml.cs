using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ImageBrowse.Models;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class GalleryView : UserControl
{
    public event Action<int>? SelectionCountChanged;

    public GalleryView()
    {
        InitializeComponent();

        GalleryListBox.SelectionChanged += (_, _) =>
        {
            SelectionCountChanged?.Invoke(GalleryListBox.SelectedItems?.Count ?? 0);
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
        EmptyState.IsVisible = isEmpty;
    }

    private void GalleryListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null) return;
        if (GalleryListBox.SelectedItem is not ImageItem item) return;

        if (item.IsFolder || item.IsParentFolder)
        {
            ViewModel.NavigateToFolder(item.FilePath);
        }
        else
        {
            ViewModel.EnterFullscreen();
        }
    }

    private void GalleryListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        if (e.Key == Key.Enter)
        {
            if (GalleryListBox.SelectedItem is ImageItem item)
            {
                if (item.IsFolder || item.IsParentFolder)
                    ViewModel.NavigateToFolder(item.FilePath);
                else
                    ViewModel.EnterFullscreen();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Back)
        {
            var parent = Path.GetDirectoryName(ViewModel.CurrentPath);
            if (!string.IsNullOrEmpty(parent))
                ViewModel.NavigateToFolder(parent);
            e.Handled = true;
        }
    }

    public void ScrollToItem(ImageItem item)
    {
        GalleryListBox.ScrollIntoView(item);
    }

    public void FocusGallery()
    {
        GalleryListBox.Focus();
    }

    #region Context Menu

    private void ContextOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedItem is not null && !ViewModel.SelectedItem.IsFolder)
            ViewModel.EnterFullscreen();
    }

    private void ContextRate1_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("1");
    private void ContextRate2_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("2");
    private void ContextRate3_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("3");
    private void ContextRate4_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("4");
    private void ContextRate5_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("5");
    private void ContextRate0_Click(object? sender, RoutedEventArgs e) => ViewModel?.SetRatingCommand.Execute("0");

    private void ContextToggleTag_Click(object? sender, RoutedEventArgs e) =>
        ViewModel?.ToggleTagCommand.Execute(null);

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
}
