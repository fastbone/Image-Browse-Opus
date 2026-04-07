namespace ImageBrowse.Services.Abstractions;

public interface IFolderThumbnailService : IDisposable
{
    event Action<string, object>? FolderThumbnailReady;

    object? GetCachedThumbnail(string folderPath, DateTime lastModified);
    void RequestThumbnail(string folderPath, DateTime lastModified);
    void CancelAll();
}
