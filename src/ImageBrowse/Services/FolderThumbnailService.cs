using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageBrowse.Services;

public sealed class FolderThumbnailService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly ConcurrentDictionary<string, byte> _inProgress = new();
    private readonly SemaphoreSlim _semaphore;
    private CancellationTokenSource _cts = new();
    private const int CompositeSize = 256;

    public event Action<string, BitmapSource>? FolderThumbnailReady;

    public FolderThumbnailService(DatabaseService db)
    {
        _db = db;
        _semaphore = new SemaphoreSlim(2, 2);
    }

    public BitmapSource? GetCachedThumbnail(string folderPath, DateTime lastModified)
    {
        var data = _db.GetThumbnail(folderPath, lastModified);
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
            var imagePaths = ImageLoadingService.GetSupportedFiles(folderPath)
                .Where(f => !ImageLoadingService.IsVideoFile(f)).Take(4).ToList();

            BitmapSource? composite = imagePaths.Count switch
            {
                0 => GenerateDefaultFolderIcon(),
                1 => LoadThumbnail(imagePaths[0], CompositeSize),
                _ => BuildComposite(imagePaths)
            };

            if (composite is null) return;

            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(composite));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var data = ms.ToArray();

            _db.SaveThumbnail(folderPath, lastModified, 0, data, 0, 0);

            using var readStream = new MemoryStream(data);
            var cached = new BitmapImage();
            cached.BeginInit();
            cached.CacheOption = BitmapCacheOption.OnLoad;
            cached.StreamSource = readStream;
            cached.EndInit();
            cached.Freeze();

            FolderThumbnailReady?.Invoke(folderPath, cached);
        }
        catch
        {
            // Skip folders that fail thumbnail generation
        }
    }

    private BitmapSource BuildComposite(List<string> imagePaths)
    {
        int half = CompositeSize / 2;
        var visual = new DrawingVisual();

        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 40)), null,
                new Rect(0, 0, CompositeSize, CompositeSize));

            var positions = new (int X, int Y)[] { (0, 0), (half, 0), (0, half), (half, half) };

            for (int i = 0; i < imagePaths.Count && i < 4; i++)
            {
                try
                {
                    var thumb = LoadThumbnail(imagePaths[i], half);
                    if (thumb is null) continue;

                    var (px, py) = positions[i];
                    double scale = Math.Min((double)half / thumb.PixelWidth, (double)half / thumb.PixelHeight);
                    double drawW = thumb.PixelWidth * scale;
                    double drawH = thumb.PixelHeight * scale;
                    double offsetX = px + (half - drawW) / 2;
                    double offsetY = py + (half - drawH) / 2;

                    ctx.DrawImage(thumb, new Rect(offsetX, offsetY, drawW, drawH));
                }
                catch
                {
                    // Skip individual images that fail
                }
            }
        }

        var rtb = new RenderTargetBitmap(CompositeSize, CompositeSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource GenerateDefaultFolderIcon()
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 40)), null,
                new Rect(0, 0, CompositeSize, CompositeSize));

            var folderColor = new SolidColorBrush(Color.FromRgb(100, 120, 160));
            var folderDark = new SolidColorBrush(Color.FromRgb(80, 100, 140));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(60, 80, 120)), 1);

            double m = CompositeSize * 0.15;
            double w = CompositeSize - 2 * m;
            double h = CompositeSize * 0.55;
            double top = CompositeSize * 0.28;
            double tabW = w * 0.35;
            double tabH = CompositeSize * 0.08;

            ctx.DrawRoundedRectangle(folderDark, pen,
                new Rect(m, top - tabH, tabW, tabH + 4), 4, 4);

            ctx.DrawRoundedRectangle(folderColor, pen,
                new Rect(m, top, w, h), 6, 6);
        }

        var rtb = new RenderTargetBitmap(CompositeSize, CompositeSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static readonly FrozenSet<string> WpfNativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico", ".jfif"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> JpegExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static BitmapSource? LoadThumbnail(string filePath, int maxDim)
    {
        var ext = Path.GetExtension(filePath);
        if (WpfNativeExtensions.Contains(ext))
        {
            var wpfResult = LoadThumbnailWithWpf(filePath, maxDim);
            if (wpfResult is not null)
                return wpfResult;
        }

        return LoadThumbnailWithMagick(filePath, maxDim);
    }

    private static BitmapSource? LoadThumbnailWithWpf(string filePath, int maxDim)
    {
        try
        {
            int orientation = ExifOrientationService.ReadOrientation(filePath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = maxDim;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return ExifOrientationService.ApplyOrientation(bitmap, orientation);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadThumbnailWithMagick(string filePath, int maxDim)
    {
        try
        {
            var settings = new ImageMagick.MagickReadSettings();
            var ext = Path.GetExtension(filePath);
            if (JpegExtensions.Contains(ext))
            {
                settings.SetDefines(new ImageMagick.Formats.JpegReadDefines
                {
                    Size = new ImageMagick.MagickGeometry((uint)maxDim * 2, (uint)maxDim * 2)
                });
            }

            using var image = new ImageMagick.MagickImage(filePath, settings);
            image.AutoOrient();
            image.Thumbnail((uint)maxDim, (uint)maxDim);

            var colorProfile = image.GetColorProfile();
            if (colorProfile is not null)
                image.TransformColorSpace(colorProfile, ImageMagick.ColorProfiles.SRGB);
            if (image.ColorSpace == ImageMagick.ColorSpace.CMYK)
                image.ColorSpace = ImageMagick.ColorSpace.sRGB;

            var data = image.ToByteArray(ImageMagick.MagickFormat.Png);
            using var stream = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
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
