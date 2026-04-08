using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ImageBrowse.Models;
using ImageBrowse.Services;
using ImageBrowse.ViewModels;

namespace ImageBrowse.Views;

public partial class SettingsDialog : Window
{
    private readonly MainViewModel _vm = null!;
    private SortField _defaultSortField;
    private SortDirection _defaultSortDirection;

    public SettingsDialog() { InitializeComponent(); }

    public SettingsDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        if (_vm is null) return;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _vm.Settings;

        StartupFolderBox.Text = s.StartupFolder;

        _defaultSortField = s.DefaultSortField;
        _defaultSortDirection = s.DefaultSortDirection;
        SyncSortFieldCombo();
        SyncSortDirButton();

        if (s.IsDarkTheme) DarkRadio.IsChecked = true;
        else LightRadio.IsChecked = true;

        EnableAnimationsCheck.IsChecked = s.EnableAnimations;
        ConfirmDeleteCheck.IsChecked = s.ConfirmBeforeDelete;
        BossModeCheck.IsChecked = s.BossModeEnabled;
        AutoUpdateCheck.IsChecked = s.CheckForUpdatesOnStartup;
        FileOperationsCheck.IsChecked = s.FileOperationsEnabled;

        UpdateCacheSize();
    }

    private void SyncSortFieldCombo()
    {
        for (int i = 0; i < DefaultSortFieldCombo.Items.Count; i++)
        {
            if (DefaultSortFieldCombo.Items[i] is ComboBoxItem ci &&
                ci.Tag is SortField field && field == _defaultSortField)
            {
                DefaultSortFieldCombo.SelectedIndex = i;
                break;
            }
        }
    }

    private void SyncSortDirButton()
    {
        DefaultSortDirButton.Content = _defaultSortDirection == SortDirection.Ascending ? "↑ Asc" : "↓ Desc";
    }

    private void ToggleDefaultSortDir_Click(object? sender, RoutedEventArgs e)
    {
        _defaultSortDirection = _defaultSortDirection == SortDirection.Ascending
            ? SortDirection.Descending : SortDirection.Ascending;
        SyncSortDirButton();
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Startup Folder",
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            StartupFolderBox.Text = result[0].Path.LocalPath;
        }
    }

    private void ClearCache_Click(object? sender, RoutedEventArgs e)
    {
        int cleared = _vm.Database.ClearAllThumbnails();
        CacheSizeText.Text = $"Cleared {cleared} thumbnails";
    }

    private void UpdateCacheSize()
    {
        CacheSizeText.Text = "";
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        var s = _vm.Settings;

        s.StartupFolder = StartupFolderBox.Text ?? "";

        if (DefaultSortFieldCombo.SelectedItem is ComboBoxItem ci && ci.Tag is SortField field)
            s.DefaultSortField = field;
        s.DefaultSortDirection = _defaultSortDirection;

        s.IsDarkTheme = DarkRadio.IsChecked == true;
        _vm.IsDarkTheme = s.IsDarkTheme;
        s.EnableAnimations = EnableAnimationsCheck.IsChecked == true;
        s.ConfirmBeforeDelete = ConfirmDeleteCheck.IsChecked == true;
        s.BossModeEnabled = BossModeCheck.IsChecked == true;
        s.CheckForUpdatesOnStartup = AutoUpdateCheck.IsChecked == true;
        s.FileOperationsEnabled = FileOperationsCheck.IsChecked == true;

        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
