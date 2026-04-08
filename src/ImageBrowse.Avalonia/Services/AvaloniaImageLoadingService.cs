using System.IO;
using Avalonia.Media.Imaging;
using ImageBrowse.Services.Abstractions;
using ImageMagick;

namespace ImageBrowse.Services;

public sealed class AvaloniaImageLoadingService : IImageLoadingService
{
    public object? LoadFullImage(string filePath, int maxDimension = 0)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            if (IsAvaloniaSupported(ext))
                return LoadNative(filePath, maxDimension);

            return LoadWithMagick(filePath, maxDimension);
        }
        catch
        {
            return LoadWithMagick(filePath, maxDimension);
        }
    }

    private static bool IsAvaloniaSupported(string ext)
    {
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jfif", StringComparison.OrdinalIgnoreCase);
    }

    private static Bitmap? LoadNative(string filePath, int maxDimension)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (maxDimension > 0)
                return Bitmap.DecodeToWidth(stream, maxDimension);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadWithMagick(string filePath, int maxDimension)
    {
        try
        {
            using var image = new MagickImage(filePath);

            if (maxDimension > 0 && (image.Width > maxDimension || image.Height > maxDimension))
                image.Thumbnail((uint)maxDimension, (uint)maxDimension);

            image.AutoOrient();

            var colorProfile = image.GetColorProfile();
            if (colorProfile is not null)
                image.TransformColorSpace(colorProfile, ColorProfiles.SRGB);
            if (image.ColorSpace == ColorSpace.CMYK)
                image.ColorSpace = ColorSpace.sRGB;

            var data = image.ToByteArray(MagickFormat.Png);
            using var ms = new MemoryStream(data);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }
}
