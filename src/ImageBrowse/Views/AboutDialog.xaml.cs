using ImageBrowse.Services;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace ImageBrowse.Views;

public partial class AboutDialog : Window
{
    private readonly UpdateService _updateService;

    public AboutDialog(UpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;

        var infoVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        VersionText.Text = $"Version {infoVersion ?? "unknown"}";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_updateService.IsInstalled)
        {
            UpdateStatusText.Text = "Updates unavailable (dev build)";
            return;
        }

        UpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking for updates...";

        var newVersion = await _updateService.CheckForUpdatesAsync();
        if (newVersion is not null)
        {
            UpdateStatusText.Text = $"Version {newVersion} available! Downloading...";
            var applied = await _updateService.DownloadAndApplyAsync(p =>
            {
                Dispatcher.Invoke(() => UpdateStatusText.Text = $"Downloading... {p}%");
            });
            if (!applied)
                UpdateStatusText.Text = "Update download failed. Try again later.";
        }
        else
        {
            UpdateStatusText.Text = "You're running the latest version.";
        }

        UpdateButton.IsEnabled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
