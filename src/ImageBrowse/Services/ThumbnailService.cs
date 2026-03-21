using ImageMagick;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageBrowse.Services;

public sealed class ThumbnailService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly ConcurrentDictionary<string, byte> _inProgress = new();
    private readonly SemaphoreSlim _semaphore;
    private CancellationTokenSource _cts = new();
    private const int ThumbnailSize = 256;

    public event Action<string, BitmapSource, int, int>? ThumbnailReady;

    public ThumbnailService(DatabaseService db)
    {
        _db = db;
        int workerCount = Math.Max(2, Environment.ProcessorCount / 2);
        _semaphore = new SemaphoreSlim(workerCount, workerCount);
    }

    public BitmapSource? GetCachedThumbnail(string filePath, DateTime lastModified)
    {
        var data = _db.GetThumbnail(filePath, lastModified);
        if (data is null) return null;

        try
        {
            using var stream = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void RequestThumbnail(string filePath, DateTime lastModified, long fileSize)
    {
        if (!_inProgress.TryAdd(filePath, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _semaphore.WaitAsync(_cts.Token);
                try
                {
                    if (_cts.Token.IsCancellationRequested) return;
                    GenerateAndCache(filePath, lastModified, fileSize);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _inProgress.TryRemove(filePath, out _);
            }
        }, _cts.Token);
    }

    private void GenerateAndCache(string filePath, DateTime lastModified, long fileSize)
    {
        try
        {
            string? contentHash = null;
            try { contentHash = ContentHashService.ComputeHash(filePath, fileSize); } catch { }

            if (contentHash is not null)
            {
                var hashHit = _db.GetThumbnailByHash(contentHash);
                if (hashHit is not null)
                {
                    _db.SaveThumbnail(filePath, lastModified, fileSize,
                        hashHit.Value.Data, hashHit.Value.Width, hashHit.Value.Height, contentHash);

                    using var hs = new MemoryStream(hashHit.Value.Data);
                    var hbmp = new BitmapImage();
                    hbmp.BeginInit();
                    hbmp.CacheOption = BitmapCacheOption.OnLoad;
                    hbmp.StreamSource = hs;
                    hbmp.EndInit();
                    hbmp.Freeze();
                    ThumbnailReady?.Invoke(filePath, hbmp, hashHit.Value.Width, hashHit.Value.Height);
                    return;
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

            if (thumbnailData.Length == 0) return;

            _db.SaveThumbnail(filePath, lastModified, fileSize, thumbnailData, width, height, contentHash);

            using var stream = new MemoryStream(thumbnailData);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            ThumbnailReady?.Invoke(filePath, bitmap, width, height);
        }
        catch
        {
            // Silently skip files that can't generate thumbnails
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
