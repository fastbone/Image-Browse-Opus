using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using ImageBrowse.Services.Abstractions;
using ImageMagick;

namespace ImageBrowse.Services;

public sealed class AvaloniaFolderThumbnailService : IFolderThumbnailService
{
    private readonly DatabaseService _db;
    private readonly ConcurrentDictionary<string, byte> _inProgress = new();
    private readonly SemaphoreSlim _semaphore;
    private CancellationTokenSource _cts = new();
    private const int CompositeSize = 256;

    public event Action<string, object>? FolderThumbnailReady;

    public AvaloniaFolderThumbnailService(DatabaseService db)
    {
        _db = db;
        _semaphore = new SemaphoreSlim(2, 2);
    }

    public object? GetCachedThumbnail(string folderPath, DateTime lastModified)
    {
        var data = _db.GetThumbnail(folderPath, lastModified);
        if (data is null) return null;

        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public void RequestThumbnail(string folderPath, DateTime lastModified)
    {
        if (!_inProgress.TryAdd(folderPath, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _semaphore.WaitAsync(_cts.Token);
                try
                {
                    if (_cts.Token.IsCancellationRequested) return;
                    GenerateAndCache(folderPath, lastModified);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _inProgress.TryRemove(folderPath, out _);
            }
        }, _cts.Token);
    }

    private void GenerateAndCache(string folderPath, DateTime lastModified)
    {
        try
        {
            var imagePaths = SupportedFormats.GetSupportedFiles(folderPath)
                .Where(f => !SupportedFormats.IsVideoFile(f)).Take(4).ToList();

            byte[] compositeData;
            if (imagePaths.Count == 0)
            {
                compositeData = GenerateDefaultFolderIcon();
            }
            else if (imagePaths.Count == 1)
            {
                compositeData = GenerateSingleThumbnail(imagePaths[0]);
            }
            else
            {
                compositeData = BuildComposite(imagePaths);
            }

            if (compositeData.Length == 0) return;

            _db.SaveThumbnail(folderPath, lastModified, 0, compositeData, 0, 0);

            using var readStream = new MemoryStream(compositeData);
            var cached = new Bitmap(readStream);
            FolderThumbnailReady?.Invoke(folderPath, cached);
        }
        catch { }
    }

    private static byte[] GenerateSingleThumbnail(string filePath)
    {
        try
        {
            using var image = new MagickImage(filePath);
            image.AutoOrient();
            image.Thumbnail(CompositeSize, CompositeSize);
            image.Quality = 85;
            return image.ToByteArray(MagickFormat.Jpeg);
        }
        catch
        {
            return [];
        }
    }

    private static byte[] BuildComposite(List<string> imagePaths)
    {
        try
        {
            using var composite = new MagickImage(MagickColors.DarkSlateGray, CompositeSize, CompositeSize);
            int half = CompositeSize / 2;

            var positions = new (int X, int Y)[] { (0, 0), (half, 0), (0, half), (half, half) };

            for (int i = 0; i < imagePaths.Count && i < 4; i++)
            {
                try
                {
                    using var thumb = new MagickImage(imagePaths[i]);
                    thumb.AutoOrient();
                    thumb.Thumbnail((uint)half, (uint)half);
                    thumb.BackgroundColor = MagickColors.Transparent;
                    thumb.Extent((uint)half, (uint)half, Gravity.Center);
                    composite.Composite(thumb, positions[i].X, positions[i].Y, CompositeOperator.Over);
                }
                catch { }
            }

            composite.Quality = 85;
            return composite.ToByteArray(MagickFormat.Jpeg);
        }
        catch
        {
            return [];
        }
    }

    private static byte[] GenerateDefaultFolderIcon()
    {
        try
        {
            using var image = new MagickImage(MagickColors.DarkSlateGray, CompositeSize, CompositeSize);

            using var body = new MagickImage(new MagickColor("#647898"),
                (uint)(CompositeSize * 0.70), (uint)(CompositeSize * 0.55));
            image.Composite(body, (int)(CompositeSize * 0.15), (int)(CompositeSize * 0.28), CompositeOperator.Over);

            using var tab = new MagickImage(new MagickColor("#506490"),
                (uint)(CompositeSize * 0.35), (uint)(CompositeSize * 0.12));
            image.Composite(tab, (int)(CompositeSize * 0.15), (int)(CompositeSize * 0.20), CompositeOperator.Over);

            image.Quality = 85;
            return image.ToByteArray(MagickFormat.Jpeg);
        }
        catch
        {
            return [];
        }
    }

    public void CancelAll()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _inProgress.Clear();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _semaphore.Dispose();
    }
}
