using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ImageBrowse.Helpers;
using ImageBrowse.Services;
using ImageBrowse.Services.Abstractions;
using ImageBrowse.ViewModels;
using ImageBrowse.Views;
using LibVLCSharp.Shared;

namespace ImageBrowse;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Core.Initialize();

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

            var cliArgs = desktop.Args ?? [];
            string? startupPath = cliArgs.Length > 0 ? cliArgs[0] : null;
            desktop.MainWindow = new MainWindow(vm, startupPath);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
