using System.Collections.Frozen;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ImageBrowse.Services;

public sealed class ImageLoadingService
{
    private static readonly FrozenSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp",
        ".heic", ".heif", ".avif", ".jxl", ".svg", ".ico", ".cur",
        ".psd", ".psb", ".xcf", ".tga", ".dds", ".hdr", ".exr",
        ".pcx", ".pbm", ".pgm", ".ppm", ".pnm",
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".sr2", ".srf",
        ".orf", ".raf", ".rw2", ".rwl", ".pef", ".dng", ".mrw", ".x3f",
        ".srw", ".3fr", ".dcr", ".kdc", ".erf", ".mos", ".mef",
        ".jp2", ".j2k", ".jpf", ".jpm", ".jpg2",
        ".eps", ".ai", ".wmf", ".emf",
        ".raw", ".bay", ".cap", ".iiq", ".ptx",
        ".rgbe", ".pfm", ".fits", ".fit", ".fts",
        ".dpx", ".cin", ".sgi", ".mng", ".apng",
        ".jfif", ".wbmp", ".xbm", ".xpm"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".m4v",
        ".mpg", ".mpeg", ".ts", ".3gp", ".ogv", ".vob", ".mts", ".m2ts"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && (SupportedExtensions.Contains(ext) || VideoExtensions.Contains(ext));
    }

    public static bool IsVideoFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }

    public static IEnumerable<string> GetSupportedFiles(string directoryPath)
    {
        if (!System.IO.Directory.Exists(directoryPath)) yield break;

        IEnumerable<string> files;
        try
        {
            files = System.IO.Directory.EnumerateFiles(directoryPath);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (IsSupported(file))
                yield return file;
        }
    }

    private static readonly FrozenSet<string> WpfNativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico", ".jfif"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public BitmapSource? LoadFullImage(string filePath, int maxDimension = 0)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            if (WpfNativeExtensions.Contains(ext))
                return LoadWithWpfNative(filePath, maxDimension);

            return LoadWithMagick(filePath, maxDimension);
        }
        catch
        {
            return LoadWithMagick(filePath, maxDimension);
        }
    }

    private static BitmapSource? LoadWithWpfNative(string filePath, int maxDimension)
    {
        try
        {
            int orientation = ExifOrientationService.ReadOrientation(filePath);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            if (maxDimension > 0)
                bitmap.DecodePixelWidth = maxDimension;
            bitmap.EndInit();
            bitmap.Freeze();

            return ExifOrientationService.ApplyOrientation(bitmap, orientation);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadWithMagick(string filePath, int maxDimension)
    {
        try
        {
            using var image = new MagickImage(filePath);

            if (maxDimension > 0 && (image.Width > maxDimension || image.Height > maxDimension))
            {
                image.Thumbnail((uint)maxDimension, (uint)maxDimension);
            }

            image.AutoOrient();

            var colorProfile = image.GetColorProfile();
            if (colorProfile is not null)
                image.TransformColorSpace(colorProfile, ColorProfiles.SRGB);
            if (image.ColorSpace == ColorSpace.CMYK)
                image.ColorSpace = ColorSpace.sRGB;

            var data = image.ToByteArray(MagickFormat.Bmp);
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
}
