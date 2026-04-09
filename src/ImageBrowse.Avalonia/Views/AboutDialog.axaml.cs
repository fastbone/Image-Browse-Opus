using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageBrowse.Models;
using ImageBrowse.Services;

namespace ImageBrowse.Views;

public partial class AboutDialog : Window
{
    private static readonly Uri SiteUri = new("https://fastbone.github.io/Image-Browse-Opus/");
    private static readonly Uri RepoUri = new("https://github.com/fastbone/Image-Browse-Opus");
    private static readonly Uri LicenseUri = new("https://github.com/fastbone/Image-Browse-Opus/blob/main/LICENSE");

    private readonly UpdateService? _updates;
    private readonly Action? _markUpdateReadyOnExit;

    public AboutDialog() : this(null, null) { }

    public AboutDialog(UpdateService? updates = null, Action? markUpdateReadyOnExit = null)
    {
        InitializeComponent();
        _updates = updates;
        _markUpdateReadyOnExit = markUpdateReadyOnExit;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "0.0.0"}";
    }

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (_updates is null) return;

        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.IsVisible = true;
        UpdateStatusText.Text = "Checking…";

        try
        {
            var newVersion = await _updates.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                UpdateStatusText.Text = "You are up to date.";
                return;
            }

            var dialog = new UpdatePromptDialog(newVersion);
            await dialog.ShowDialog(this);

            switch (dialog.Result)
            {
                case UpdatePromptResult.InstallNow:
                    UpdateStatusText.Text = "Downloading…";
                    var ok = await _updates.DownloadAsync(p =>
                    {
                        Dispatcher.UIThread.Post(() =>
                            UpdateStatusText.Text = $"Downloading… {p}%");
                    });
                    if (ok)
                        _updates.ApplyAndRestart();
                    else
                        UpdateStatusText.Text = "Download failed.";
                    break;

                case UpdatePromptResult.InstallOnClose:
                    UpdateStatusText.Text = "Downloading…";
                    var ok2 = await _updates.DownloadAsync(p =>
                    {
                        Dispatcher.UIThread.Post(() =>
                            UpdateStatusText.Text = $"Downloading… {p}%");
                    });
                    if (ok2)
                    {
                        _markUpdateReadyOnExit?.Invoke();
                        UpdateStatusText.Text = "Update will install when you close the app.";
                    }
                    else
                        UpdateStatusText.Text = "Download failed.";
                    break;

                default:
                    UpdateStatusText.Text = "";
                    UpdateStatusText.IsVisible = false;
                    break;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Update check failed: {ex.Message}";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void SiteLink_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(SiteUri);
    }

    private async void RepoLink_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(RepoUri);
    }

    private async void LicenseLink_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(LicenseUri);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
}
