using ImageBrowse.Models;
using ImageBrowse.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImageBrowse.Views;

public partial class SettingsDialog : Window
{
    private readonly MainViewModel _vm;
    private SortField _defaultSortField;
    private SortDirection _defaultSortDirection;

    public SettingsDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        StartupFolderBox.Text = vm.Settings.StartupFolder;

        _defaultSortField = vm.Settings.DefaultSortField;
        _defaultSortDirection = vm.Settings.DefaultSortDirection;
        SelectSortFieldInCombo(_defaultSortField);
        UpdateSortDirButton();

        if (vm.IsDarkTheme)
            DarkRadio.IsChecked = true;
        else
            LightRadio.IsChecked = true;

        Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush");
    }

    private void SelectSortFieldInCombo(SortField field)
    {
        foreach (ComboBoxItem item in DefaultSortFieldCombo.Items)
        {
            if (item.Tag is SortField f && f == field)
            {
                DefaultSortFieldCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void UpdateSortDirButton()
    {
        DefaultSortDirButton.Content = _defaultSortDirection == SortDirection.Ascending ? "\u25B2 Ascending" : "\u25BC Descending";
    }

    private void ToggleDefaultSortDir_Click(object sender, RoutedEventArgs e)
    {
        _defaultSortDirection = _defaultSortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;
        UpdateSortDirButton();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Startup Folder",
            InitialDirectory = StartupFolderBox.Text
        };

        if (dialog.ShowDialog(this) == true)
        {
            StartupFolderBox.Text = dialog.FolderName;
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var folder = StartupFolderBox.Text.Trim();
        if (!string.IsNullOrEmpty(folder))
            _vm.Settings.StartupFolder = folder;

        if (DefaultSortFieldCombo.SelectedItem is ComboBoxItem comboItem && comboItem.Tag is SortField field)
            _defaultSortField = field;

        _vm.Settings.DefaultSortField = _defaultSortField;
        _vm.Settings.DefaultSortDirection = _defaultSortDirection;

        bool isDark = DarkRadio.IsChecked == true;
        _vm.IsDarkTheme = isDark;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        int count = _vm.Database.ClearAllThumbnails();
        MessageBox.Show(this,
            $"Cleared {count:N0} cached thumbnail{(count != 1 ? "s" : "")}.\nThumbnails will regenerate as you browse.",
            "Thumbnail Cache Cleared",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
