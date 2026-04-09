using ImageMagick;

namespace ImageBrowse.Services;

/// <summary>Encodes BGRA video frame buffers to JPEG (cross-platform; used by Avalonia video thumbnails).</summary>
public static class VideoFrameJpegEncoder
{
    public static byte[] EncodeBgraToJpeg(byte[] bgra, int width, int height, int stride, int quality = 85)
    {
        byte[] packed = bgra;
        int expectedStride = width * 4;
        if (stride != expectedStride)
        {
            packed = new byte[expectedStride * height];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(bgra, y * stride, packed, y * expectedStride, expectedStride);
        }

        var readSettings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, "BGRA");

        using var image = new MagickImage();
        image.ReadPixels(packed, readSettings);
        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)quality;
        return image.ToByteArray();
    }
}
