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
}
