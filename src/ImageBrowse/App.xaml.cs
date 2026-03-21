using System.Windows;
using LibVLCSharp.Shared;

namespace ImageBrowse;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        Core.Initialize();
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception.Message}",
                "Image Browse - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        string? startupPath = e.Args.Length > 0 ? e.Args[0] : null;
        var mainWindow = new MainWindow(startupPath);
        mainWindow.Show();
    }
}
