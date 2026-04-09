using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageBrowse.Services;

public static class ExifOrientationService
{
    public static int ReadOrientation(string filePath) => ExifOrientationReader.ReadOrientation(filePath);

    public static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        if (orientation <= 1 || orientation > 8)
            return source;

        var transform = orientation switch
        {
            2 => new TransformGroup { Children = { new ScaleTransform(-1, 1) } },
            3 => new TransformGroup { Children = { new RotateTransform(180) } },
            4 => new TransformGroup { Children = { new ScaleTransform(1, -1) } },
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new TransformGroup { Children = { new RotateTransform(90) } },
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new TransformGroup { Children = { new RotateTransform(270) } },
            _ => (Transform?)null
        };

        if (transform is null)
            return source;

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }
}
