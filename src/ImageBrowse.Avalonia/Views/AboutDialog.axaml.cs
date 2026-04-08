using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace ImageBrowse.Views;

public partial class AboutDialog : Window
{
    private static readonly Uri SiteUri = new("https://fastbone.github.io/Image-Browse-Opus/");
    private static readonly Uri RepoUri = new("https://github.com/fastbone/Image-Browse-Opus");
    private static readonly Uri LicenseUri = new("https://github.com/fastbone/Image-Browse-Opus/blob/main/LICENSE");

    public AboutDialog()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "0.0.0"}";
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
