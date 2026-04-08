namespace ImageBrowse.Services.Abstractions;

public interface IDispatcherService
{
    void BeginInvoke(Action action);
}
