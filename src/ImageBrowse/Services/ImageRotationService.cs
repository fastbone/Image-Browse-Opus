using ImageMagick;
using System.IO;

namespace ImageBrowse.Services;

public static class ImageRotationService
{
    public static (int NewWidth, int NewHeight) RotateClockwise90(string filePath)
    {
        return RotateWithRetry(filePath, retryOnce: true);
    }

    private static (int NewWidth, int NewHeight) RotateWithRetry(string filePath, bool retryOnce)
    {
        try
        {
            using var image = new MagickImage(filePath);
            image.AutoOrient();
            image.Rotate(90);

            var ext = Path.GetExtension(filePath);
            if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                image.Quality = 100;

            image.RemoveProfile("exif");
            image.Write(filePath);

            return ((int)image.Width, (int)image.Height);
        }
        catch (IOException) when (retryOnce)
        {
            Thread.Sleep(200);
            return RotateWithRetry(filePath, retryOnce: false);
        }
    }
}
