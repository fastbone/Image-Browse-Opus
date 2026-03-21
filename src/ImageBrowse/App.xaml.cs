using System.Windows;

namespace ImageBrowse;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception.Message}",
                "Image Browse - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
