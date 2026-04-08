namespace ImageBrowse.Services.Abstractions;

public interface IThumbnailService : IDisposable
{
    event Action<string, object, int, int>? ThumbnailReady;
    event Action<string, object, int, int, TimeSpan>? VideoThumbnailReady;
    event Action<string>? ThumbnailFailed;

    object? GetCachedThumbnail(string filePath, DateTime lastModified);
    void RequestThumbnail(string filePath, DateTime lastModified, long fileSize);
    void CancelAll();
}
