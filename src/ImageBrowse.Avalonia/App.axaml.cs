using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ImageBrowse.Helpers;
using ImageBrowse.Services;
using ImageBrowse.Services.Abstractions;
using ImageBrowse.ViewModels;
using ImageBrowse.Views;

namespace ImageBrowse;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var db = new DatabaseService();
            var settings = new SettingsService(db);
            var imageLoader = new AvaloniaImageLoadingService();

            var vm = new MainViewModel(
                db, settings,
                new AvaloniaThumbnailService(db),
                new AvaloniaFolderThumbnailService(db),
                new MetadataService(db),
                imageLoader,
                new AvaloniaDispatcherService(),
                ManagedNaturalSortComparer.Instance);

            desktop.MainWindow = new MainWindow(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
