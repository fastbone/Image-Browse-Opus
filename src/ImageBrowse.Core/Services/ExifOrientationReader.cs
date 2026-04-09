using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace ImageBrowse.Services;

/// <summary>Reads EXIF orientation (tag 0x0112) without UI dependencies.</summary>
public static class ExifOrientationReader
{
    public static int ReadOrientation(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out int orientation))
                return orientation;
        }
        catch { }

        return 1;
    }
}
