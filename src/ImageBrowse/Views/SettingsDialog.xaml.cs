using ImageBrowse.Models;
using ImageBrowse.Services;
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

    private readonly List<(CheckBox CheckBox, string[] Extensions)> _formatCheckBoxes = [];
    private bool _suppressContextMenuEvent;

    private static readonly (string Label, (string Name, string[] Extensions)[] Formats)[] FormatGroups =
    [
        ("Common", [
            ("JPEG", [".jpg", ".jpeg", ".jfif"]),
            ("PNG", [".png"]),
            ("GIF", [".gif"]),
            ("BMP", [".bmp"]),
            ("WebP", [".webp"]),
            ("ICO", [".ico"]),
        ]),
        ("Modern", [
            ("HEIC/HEIF", [".heic", ".heif"]),
            ("AVIF", [".avif"]),
            ("JPEG XL", [".jxl"]),
            ("APNG", [".apng"]),
            ("JPEG 2000", [".jp2", ".j2k", ".jpf", ".jpm", ".jpg2"]),
        ]),
        ("Professional", [
            ("TIFF", [".tiff", ".tif"]),
            ("PSD", [".psd", ".psb"]),
            ("OpenEXR", [".exr"]),
            ("HDR", [".hdr", ".rgbe"]),
            ("TGA", [".tga"]),
            ("DDS", [".dds"]),
            ("SVG", [".svg"]),
            ("XCF", [".xcf"]),
        ]),
        ("RAW", [
            ("Canon (CR2/CR3)", [".cr2", ".cr3", ".crw"]),
            ("Nikon (NEF)", [".nef", ".nrw"]),
            ("Sony (ARW)", [".arw", ".sr2", ".srf"]),
            ("Adobe DNG", [".dng"]),
            ("Olympus (ORF)", [".orf"]),
            ("Fuji (RAF)", [".raf"]),
            ("Panasonic (RW2)", [".rw2", ".rwl"]),
            ("Pentax (PEF)", [".pef"]),
            ("Samsung (SRW)", [".srw"]),
            ("Other RAW", [".mrw", ".x3f", ".3fr", ".dcr", ".kdc", ".erf", ".mos", ".mef",
                           ".raw", ".bay", ".cap", ".iiq", ".ptx"]),
        ]),
        ("Other", [
            ("PCX", [".pcx"]),
            ("Netpbm", [".pbm", ".pgm", ".ppm", ".pnm"]),
            ("EPS/AI", [".eps", ".ai"]),
            ("WMF/EMF", [".wmf", ".emf"]),
            ("FITS", [".fits", ".fit", ".fts"]),
            ("DPX/Cineon", [".dpx", ".cin"]),
            ("SGI", [".sgi"]),
            ("MNG", [".mng"]),
            ("PFM", [".pfm"]),
            ("WBMP", [".wbmp"]),
            ("XBM/XPM", [".xbm", ".xpm"]),
            ("CUR", [".cur"]),
        ]),
    ];

    public SettingsDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        StartupFolderBox.Text = vm.Settings.StartupFolder;

        _defaultSortField = vm.Settings.DefaultSortField;
        _defaultSortDirection = vm.Settings.DefaultSortDirection;
        SelectSortFieldInCombo(_defaultSortField);
        UpdateSortDirButton();

        ConfirmDeleteCheck.IsChecked = vm.Settings.ConfirmBeforeDelete;
        EnableAnimationsCheck.IsChecked = vm.Settings.EnableAnimations;

        if (vm.IsDarkTheme)
            DarkRadio.IsChecked = true;
        else
            LightRadio.IsChecked = true;

        _suppressContextMenuEvent = true;
        ContextMenuCheck.IsChecked = FileAssociationService.IsContextMenuRegistered();
        _suppressContextMenuEvent = false;

        PopulateFormatGroups();
        UpdateCacheSizeText();

        Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush");
    }

    private void UpdateCacheSizeText()
    {
        int count = _vm.Database.GetThumbnailCount();
        CacheSizeText.Text = count > 0 ? $"{count:N0} cached thumbnails" : "Cache is empty";
    }

    private void PopulateFormatGroups()
    {
        var registered = FileAssociationService.GetRegisteredExtensions();

        foreach (var (groupLabel, formats) in FormatGroups)
        {
            var header = new TextBlock
            {
                Text = groupLabel,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = (System.Windows.Media.Brush)FindResource("FgSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 4),
            };
            FormatGroupsPanel.Children.Add(header);

            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

            foreach (var (name, extensions) in formats)
            {
                var cb = new CheckBox
                {
                    Content = name,
                    FontSize = 12,
                    Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush"),
                    Margin = new Thickness(0, 0, 16, 4),
                    IsChecked = extensions.Any(ext => registered.Contains(ext)),
                };
                panel.Children.Add(cb);
                _formatCheckBoxes.Add((cb, extensions));
            }

            FormatGroupsPanel.Children.Add(panel);
        }
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

    private void ContextMenuCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressContextMenuEvent) return;

        try
        {
            if (ContextMenuCheck.IsChecked == true)
                FileAssociationService.RegisterContextMenu();
            else
                FileAssociationService.UnregisterContextMenu();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to update context menu:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _suppressContextMenuEvent = true;
            ContextMenuCheck.IsChecked = FileAssociationService.IsContextMenuRegistered();
            _suppressContextMenuEvent = false;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (cb, _) in _formatCheckBoxes)
            cb.IsChecked = true;
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (cb, _) in _formatCheckBoxes)
            cb.IsChecked = false;
    }

    private void ApplyAssociations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var toRegister = new List<string>();
            var toUnregister = new List<string>();

            foreach (var (cb, extensions) in _formatCheckBoxes)
            {
                if (cb.IsChecked == true)
                    toRegister.AddRange(extensions);
                else
                    toUnregister.AddRange(extensions);
            }

            if (toUnregister.Count > 0)
                FileAssociationService.UnregisterFileAssociations(toUnregister);
            if (toRegister.Count > 0)
                FileAssociationService.RegisterFileAssociations(toRegister);

            _vm.Settings.RegisteredExtensions = string.Join(",", toRegister);

            MessageBox.Show(this,
                $"Registered {toRegister.Count} file extension{(toRegister.Count != 1 ? "s" : "")}.\n\n" +
                "The app will now appear in the 'Open With' menu for these formats. " +
                "To set it as the default, use Windows Settings > Default Apps.",
                "File Associations Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to update file associations:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _vm.Settings.ConfirmBeforeDelete = ConfirmDeleteCheck.IsChecked == true;
        _vm.Settings.EnableAnimations = EnableAnimationsCheck.IsChecked == true;

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
        UpdateCacheSizeText();
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
