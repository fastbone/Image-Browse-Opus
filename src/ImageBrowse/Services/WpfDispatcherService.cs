using System.Windows;
using ImageBrowse.Services.Abstractions;

namespace ImageBrowse.Services;

public sealed class WpfDispatcherService : IDispatcherService
{
    public void BeginInvoke(Action action)
    {
        Application.Current?.Dispatcher.BeginInvoke(action);
    }
}
