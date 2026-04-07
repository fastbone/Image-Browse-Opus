using System.Collections.Frozen;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageBrowse.Services.Abstractions;
using ImageMagick;

namespace ImageBrowse.Services;

public sealed class ImageLoadingService : IImageLoadingService
{
    private static readonly FrozenSet<string> WpfNativeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico", ".jfif"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public object? LoadFullImage(string filePath, int maxDimension = 0)
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
