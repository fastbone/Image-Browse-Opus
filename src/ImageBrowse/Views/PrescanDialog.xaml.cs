using ImageBrowse.Helpers;
using ImageBrowse.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImageBrowse.Views;

public partial class PrescanDialog : Window
{
    private readonly DatabaseService _db;
    private readonly PrescanService _prescanService;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    private readonly bool _enableAnimations;

    public PrescanDialog(DatabaseService db, string currentPath, bool enableAnimations = true)
    {
        InitializeComponent();
        _db = db;
        _enableAnimations = enableAnimations;
        _prescanService = new PrescanService();
        _prescanService.ProgressChanged += OnProgressChanged;

        FolderBox.Text = currentPath;
        StatusLabel.Text = "Ready";
        StatsText.Text = "";
        CurrentFolderText.Text = "";

        Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush");

        Loaded += (_, _) => DialogAnimationHelper.AnimateOpen(this, _enableAnimations);
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder to Prescan",
            InitialDirectory = FolderBox.Text
        };

        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _cts?.Cancel();
            StartButton.Content = "Cancelling...";
            StartButton.IsEnabled = false;
            return;
        }

        var folder = FolderBox.Text.Trim();
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
        {
            StatusLabel.Text = "Invalid folder path.";
            return;
        }

        int depth = -1;
        if (DepthCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            int.TryParse(tag, out depth);

        _isRunning = true;
        _cts = new CancellationTokenSource();
        StartButton.Content = "Cancel";
        CloseButton.IsEnabled = false;
        FolderBox.IsEnabled = false;
        DepthCombo.IsEnabled = false;
        StatusLabel.Text = "Scanning...";
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = true;

        try
        {
            await _prescanService.RunPrescanAsync(folder, depth, _db, _cts.Token);
            StatusLabel.Text = "Prescan complete!";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Prescan cancelled.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _isRunning = false;
            _cts?.Dispose();
            _cts = null;
            StartButton.Content = "Start Prescan";
            StartButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
            FolderBox.IsEnabled = true;
            DepthCombo.IsEnabled = true;
            ProgressBar.IsIndeterminate = false;
        }
    }

    private void OnProgressChanged(PrescanProgress p)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (p.FilesTotal > 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = p.FilesTotal;
                ProgressBar.Value = p.FilesProcessed;
            }

            CurrentFolderText.Text = p.CurrentFolder;
            StatsText.Text = $"Folders: {p.FoldersScanned}/{p.TotalFolders}  |  " +
                             $"Files: {p.FilesProcessed:N0}/{p.FilesTotal:N0}  |  " +
                             $"Cache hits: {p.CacheHits:N0}  |  " +
                             $"New: {p.NewThumbnails:N0}";
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _cts?.Cancel();
        }
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isRunning)
                _cts?.Cancel();
            else
                Close();
        }
    }
}
