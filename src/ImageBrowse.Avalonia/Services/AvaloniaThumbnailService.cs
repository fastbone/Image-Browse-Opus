using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO;
using Avalonia.Media.Imaging;
using ImageBrowse.Services.Abstractions;
using ImageMagick;
using ImageMagick.Formats;

namespace ImageBrowse.Services;

public sealed class AvaloniaThumbnailService : IThumbnailService
{
    private readonly DatabaseService _db;
    private readonly ConcurrentDictionary<string, byte> _inProgress = new();
    private readonly SemaphoreSlim _semaphore;
    private CancellationTokenSource _cts = new();
    private const int ThumbnailSize = 256;

    private static readonly FrozenSet<string> RawExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".sr2", ".srf",
        ".orf", ".raf", ".rw2", ".rwl", ".pef", ".dng", ".mrw", ".x3f",
        ".srw", ".3fr", ".dcr", ".kdc", ".erf", ".mos", ".mef",
        ".raw", ".bay", ".cap", ".iiq", ".ptx"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> JpegExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public event Action<string, object, int, int>? ThumbnailReady;
#pragma warning disable CS0067 // Video thumbnails not implemented on Avalonia yet; required by IThumbnailService (see WPF ThumbnailService.GenerateVideoThumbnail).
    public event Action<string, object, int, int, TimeSpan>? VideoThumbnailReady;
#pragma warning restore CS0067
    public event Action<string>? ThumbnailFailed;

    public AvaloniaThumbnailService(DatabaseService db)
    {
        _db = db;
        int workerCount = Math.Max(2, Environment.ProcessorCount / 2);
        _semaphore = new SemaphoreSlim(workerCount, workerCount);
    }

    public object? GetCachedThumbnail(string filePath, DateTime lastModified)
    {
        var data = _db.GetThumbnail(filePath, lastModified);
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
            if (SupportedFormats.IsVideoFile(filePath))
            {
                ThumbnailFailed?.Invoke(filePath);
                return;
            }

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
                    var hbmp = new Bitmap(hs);
                    ThumbnailReady?.Invoke(filePath, hbmp, hashHit.Value.Width, hashHit.Value.Height);
                    return;
                }
            }

            byte[] thumbnailData;
            int width, height;

            var ext = Path.GetExtension(filePath);

            if (RawExtensions.Contains(ext))
            {
                (thumbnailData, width, height) = ExtractRawEmbeddedThumbnail(filePath);
                if (thumbnailData.Length == 0)
                    (thumbnailData, width, height) = GenerateWithMagick(filePath);
            }
            else
            {
                (thumbnailData, width, height) = GenerateWithMagick(filePath);
            }

            if (thumbnailData.Length == 0) return;

            _db.SaveThumbnail(filePath, lastModified, fileSize, thumbnailData, width, height, contentHash);

            using var stream = new MemoryStream(thumbnailData);
            var bitmap = new Bitmap(stream);
            ThumbnailReady?.Invoke(filePath, bitmap, width, height);
        }
        catch
        {
            ThumbnailFailed?.Invoke(filePath);
        }
    }

    private static (byte[] Data, int Width, int Height) ExtractRawEmbeddedThumbnail(string filePath)
    {
        try
        {
            var info = new MagickImageInfo(filePath);
            int origW = (int)info.Width;
            int origH = (int)info.Height;

            using var image = new MagickImage();
            image.Ping(filePath);

            var exifProfile = image.GetExifProfile();
            var thumb = exifProfile?.CreateThumbnail();
            if (thumb is not null && thumb.Width >= 160)
            {
                thumb.AutoOrient();
                thumb.Thumbnail((uint)ThumbnailSize, (uint)ThumbnailSize);
                thumb.Quality = 85;
                var data = thumb.ToByteArray(MagickFormat.Jpeg);
                thumb.Dispose();
                return (data, origW, origH);
            }
            thumb?.Dispose();

            var dngProfile = image.GetProfile("dng:thumbnail");
            if (dngProfile is not null)
            {
                using var dngThumb = new MagickImage(dngProfile.ToByteArray());
                dngThumb.AutoOrient();
                dngThumb.Thumbnail((uint)ThumbnailSize, (uint)ThumbnailSize);
                dngThumb.Quality = 85;
                var data = dngThumb.ToByteArray(MagickFormat.Jpeg);
                return (data, origW, origH);
            }
        }
        catch { }

        return ([], 0, 0);
    }

    private static (byte[] Data, int Width, int Height) GenerateWithMagick(string filePath)
    {
        try
        {
            var settings = new MagickReadSettings();
            var ext = Path.GetExtension(filePath);
            if (JpegExtensions.Contains(ext))
            {
                settings.SetDefines(new JpegReadDefines
                {
                    Size = new MagickGeometry((uint)ThumbnailSize * 2, (uint)ThumbnailSize * 2)
                });
            }

            using var image = new MagickImage(filePath, settings);
            int origW = (int)image.Width;
            int origH = (int)image.Height;

            image.AutoOrient();

            var colorProfile = image.GetColorProfile();
            if (colorProfile is not null)
                image.TransformColorSpace(colorProfile, ColorProfiles.SRGB);
            if (image.ColorSpace == ColorSpace.CMYK)
                image.ColorSpace = ColorSpace.sRGB;

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
