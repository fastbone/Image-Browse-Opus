using ImageMagick;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImageBrowse.Services;

public record PrescanProgress(
    string CurrentFolder,
    int FoldersScanned,
    int TotalFolders,
    int FilesProcessed,
    int FilesTotal,
    int CacheHits,
    int NewThumbnails);

public sealed class PrescanService
{
    private const int ThumbnailSize = 256;

    public event Action<PrescanProgress>? ProgressChanged;

    public async Task RunPrescanAsync(string rootPath, int maxDepth,
        DatabaseService db, CancellationToken ct)
    {
        var folders = new List<string>();
        CollectFolders(rootPath, maxDepth, 0, folders, ct);

        int totalFiles = 0;
        var folderFiles = new List<(string Folder, List<string> Files)>();
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var files = ImageLoadingService.GetSupportedFiles(folder).ToList();
            folderFiles.Add((folder, files));
            totalFiles += files.Count;
        }

        int foldersScanned = 0;
        int filesProcessed = 0;
        int cacheHits = 0;
        int newThumbnails = 0;

        var semaphore = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount / 2));

        foreach (var (folder, files) in folderFiles)
        {
            ct.ThrowIfCancellationRequested();
            foldersScanned++;

            var tasks = files.Select(filePath => Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    bool wasHit = ProcessFile(filePath, db);

                    Interlocked.Increment(ref filesProcessed);
                    if (wasHit)
                        Interlocked.Increment(ref cacheHits);
                    else
                        Interlocked.Increment(ref newThumbnails);

                    if (filesProcessed % 5 == 0 || filesProcessed == totalFiles)
                    {
                        ProgressChanged?.Invoke(new PrescanProgress(
                            folder, foldersScanned, folders.Count,
                            filesProcessed, totalFiles,
                            cacheHits, newThumbnails));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct)).ToList();

            await Task.WhenAll(tasks);

            ProgressChanged?.Invoke(new PrescanProgress(
                folder, foldersScanned, folders.Count,
                filesProcessed, totalFiles,
                cacheHits, newThumbnails));
        }

        ProgressChanged?.Invoke(new PrescanProgress(
            rootPath, foldersScanned, folders.Count,
            filesProcessed, totalFiles,
            cacheHits, newThumbnails));

        semaphore.Dispose();
    }

    private static void CollectFolders(string path, int maxDepth, int currentDepth,
        List<string> result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        result.Add(path);

        if (maxDepth >= 0 && currentDepth >= maxDepth) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    CollectFolders(dir, maxDepth, currentDepth + 1, result, ct);
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool ProcessFile(string filePath, DatabaseService db)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists) return false;

            var existing = db.GetThumbnail(filePath, fi.LastWriteTime);
            if (existing is not null) return true;

            string? contentHash = null;
            try { contentHash = ContentHashService.ComputeHash(filePath, fi.Length); } catch { }

            if (contentHash is not null)
            {
                var hashHit = db.GetThumbnailByHash(contentHash);
                if (hashHit is not null)
                {
                    db.SaveThumbnail(filePath, fi.LastWriteTime, fi.Length,
                        hashHit.Value.Data, hashHit.Value.Width, hashHit.Value.Height, contentHash);
                    return true;
                }
            }

            byte[] thumbnailData;
            int width, height;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".tif")
            {
                (thumbnailData, width, height) = GenerateWithWpf(filePath);
            }
            else
            {
                (thumbnailData, width, height) = GenerateWithMagick(filePath);
            }

            if (thumbnailData.Length == 0) return false;

            db.SaveThumbnail(filePath, fi.LastWriteTime, fi.Length,
                thumbnailData, width, height, contentHash);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static (byte[] Data, int Width, int Height) GenerateWithWpf(string filePath)
    {
        try
        {
            var original = new BitmapImage();
            original.BeginInit();
            original.CacheOption = BitmapCacheOption.OnLoad;
            original.UriSource = new Uri(filePath, UriKind.Absolute);
            original.EndInit();
            original.Freeze();

            int origW = original.PixelWidth;
            int origH = original.PixelHeight;

            var thumbnail = new BitmapImage();
            thumbnail.BeginInit();
            thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            thumbnail.UriSource = new Uri(filePath, UriKind.Absolute);
            thumbnail.DecodePixelWidth = ThumbnailSize;
            thumbnail.EndInit();
            thumbnail.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return (ms.ToArray(), origW, origH);
        }
        catch
        {
            return GenerateWithMagick(filePath);
        }
    }

    private static (byte[] Data, int Width, int Height) GenerateWithMagick(string filePath)
    {
        try
        {
            using var image = new MagickImage(filePath);
            int origW = (int)image.Width;
            int origH = (int)image.Height;

            image.AutoOrient();
            image.Thumbnail((uint)ThumbnailSize, (uint)ThumbnailSize);
            image.Quality = 85;

            var data = image.ToByteArray(MagickFormat.Jpeg);
            return (data, origW, origH);
        }
        catch
        {
            return ([], 0, 0);
        }
    }
}
