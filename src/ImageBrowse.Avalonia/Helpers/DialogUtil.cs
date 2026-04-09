using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ImageBrowse.Helpers;

internal static class DialogUtil
{
    public static async Task<bool> ShowYesNoAsync(Window owner, string message, string title)
    {
        bool ok = false;
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Children = { text } }
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var yes = new Button { Content = "Yes", IsDefault = true };
        var no = new Button { Content = "No", IsCancel = true };
        yes.Click += (_, _) =>
        {
            ok = true;
            dlg.Close();
        };
        no.Click += (_, _) => dlg.Close();
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        ((StackPanel)dlg.Content!).Children.Add(buttons);
        await dlg.ShowDialog(owner);
        return ok;
    }

    public static async Task<string?> ShowRenamePromptAsync(Window owner, string initial)
    {
        var box = new TextBox { Text = initial, Width = 300 };
        string? result = null;
        var dlg = new Window
        {
            Title = "Rename",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var okBtn = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        okBtn.Click += (_, _) =>
        {
            result = box.Text?.Trim();
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task ShowMessageAsync(Window owner, string message, string title)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 },
                    new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true }
                }
            }
        };
        if (dlg.Content is StackPanel sp && sp.Children[1] is Button b)
            b.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(owner);
    }
}
