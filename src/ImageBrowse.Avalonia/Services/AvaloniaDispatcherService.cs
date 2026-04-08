using Avalonia.Threading;
using ImageBrowse.Services.Abstractions;

namespace ImageBrowse.Services;

public sealed class AvaloniaDispatcherService : IDispatcherService
{
    public void BeginInvoke(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}
