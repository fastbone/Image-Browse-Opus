using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class GalleryView : UserControl
{
    public GalleryView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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
                // TODO: Move to trash
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

    private void NavigateUp()
    {
        if (ViewModel is null || string.IsNullOrEmpty(ViewModel.CurrentPath)) return;
        var parent = System.IO.Directory.GetParent(ViewModel.CurrentPath);
        if (parent is not null)
            _ = ViewModel.NavigateToFolder(parent.FullName);
    }
}
