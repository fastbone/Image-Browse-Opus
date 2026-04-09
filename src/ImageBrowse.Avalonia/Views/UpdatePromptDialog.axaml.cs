using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ImageBrowse.Models;

namespace ImageBrowse.Views;

public partial class UpdatePromptDialog : Window
{
    public UpdatePromptResult Result { get; private set; } = UpdatePromptResult.Ignore;

    public UpdatePromptDialog() : this("0.0.0") { }

    public UpdatePromptDialog(string version)
    {
        InitializeComponent();
        MessageText.Text = $"Version {version} is available. What would you like to do?";
    }

    private void InstallNow_Click(object? sender, RoutedEventArgs e)
    {
        Result = UpdatePromptResult.InstallNow;
        Close();
    }

    private void InstallOnClose_Click(object? sender, RoutedEventArgs e)
    {
        Result = UpdatePromptResult.InstallOnClose;
        Close();
    }

    private void Ignore_Click(object? sender, RoutedEventArgs e)
    {
        Result = UpdatePromptResult.Ignore;
        Close();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = UpdatePromptResult.Ignore;
            Close();
            e.Handled = true;
        }
    }
}
