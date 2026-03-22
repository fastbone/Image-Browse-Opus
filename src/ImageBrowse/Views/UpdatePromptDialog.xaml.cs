using ImageBrowse.Helpers;
using ImageBrowse.Models;
using System.Windows;
using System.Windows.Input;

namespace ImageBrowse.Views;

public partial class UpdatePromptDialog : Window
{
    public UpdatePromptResult Result { get; private set; } = UpdatePromptResult.Ignore;

    public UpdatePromptDialog(string version, bool enableAnimations = true)
    {
        InitializeComponent();

        Background = (System.Windows.Media.Brush)FindResource("BgPrimaryBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("FgPrimaryBrush");

        MessageText.Text = $"Version {version} is available. What would you like to do?";

        Loaded += (_, _) => DialogAnimationHelper.AnimateOpen(this, enableAnimations);
    }

    private void InstallNow_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdatePromptResult.InstallNow;
        DialogResult = true;
        Close();
    }

    private void InstallOnClose_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdatePromptResult.InstallOnClose;
        DialogResult = true;
        Close();
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        Result = UpdatePromptResult.Ignore;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = UpdatePromptResult.Ignore;
            DialogResult = false;
            Close();
        }
    }
}
