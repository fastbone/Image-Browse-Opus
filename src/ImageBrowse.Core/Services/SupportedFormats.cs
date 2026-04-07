using System.Collections.Frozen;
using System.IO;

namespace ImageBrowse.Services;

public static class SupportedFormats
{
    public static readonly FrozenSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

    public static readonly FrozenSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".m4v",
        ".mpg", ".mpeg", ".ts", ".3gp", ".ogv", ".vob", ".mts", ".m2ts"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && (ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext));
    }

    public static bool IsVideoFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }

    public static IEnumerable<string> GetSupportedFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath);
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
}
