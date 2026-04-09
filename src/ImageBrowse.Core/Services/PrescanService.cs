using System.Collections.Frozen;
using System.IO;
using ImageMagick;
using ImageMagick.Formats;

namespace ImageBrowse.Services;

public record PrescanProgress(
    string CurrentFolder,
    int FoldersScanned,
    int TotalFolders,
    int FilesProcessed,
    int FilesTotal,
    int CacheHits,
    int NewThumbnails);

public sealed class PrescanService
{
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

    public event Action<PrescanProgress>? ProgressChanged;

    public async Task RunPrescanAsync(string rootPath, int maxDepth,
        DatabaseService db, CancellationToken ct)
    {
        var folders = new List<string>();
        CollectFolders(rootPath, maxDepth, 0, folders, ct);

        int totalFiles = 0;
        var folderFiles = new List<(string Folder, List<string> Files)>();
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var files = SupportedFormats.GetSupportedFiles(folder).ToList();
            folderFiles.Add((folder, files));
            totalFiles += files.Count;
        }

        int foldersScanned = 0;
        int filesProcessed = 0;
        int cacheHits = 0;
        int newThumbnails = 0;

        int maxParallel = Math.Max(2, Environment.ProcessorCount / 2);

        foreach (var (folder, files) in folderFiles)
        {
            ct.ThrowIfCancellationRequested();
            foldersScanned++;

            await Parallel.ForEachAsync(files, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallel,
                CancellationToken = ct
            }, (filePath, token) =>
            {
                token.ThrowIfCancellationRequested();
                bool wasHit = ProcessFile(filePath, db);

                Interlocked.Increment(ref filesProcessed);
                if (wasHit)
                    Interlocked.Increment(ref cacheHits);
                else
                    Interlocked.Increment(ref newThumbnails);

                if (filesProcessed % 5 == 0 || filesProcessed == totalFiles)
                {
                    ProgressChanged?.Invoke(new PrescanProgress(
                        folder, foldersScanned, folders.Count,
                        filesProcessed, totalFiles,
                        cacheHits, newThumbnails));
                }

                return ValueTask.CompletedTask;
            });

            ProgressChanged?.Invoke(new PrescanProgress(
                folder, foldersScanned, folders.Count,
                filesProcessed, totalFiles,
                cacheHits, newThumbnails));
        }

        ProgressChanged?.Invoke(new PrescanProgress(
            rootPath, foldersScanned, folders.Count,
            filesProcessed, totalFiles,
            cacheHits, newThumbnails));
    }

    private static void CollectFolders(string path, int maxDepth, int currentDepth,
        List<string> result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        result.Add(path);

        if (maxDepth >= 0 && currentDepth >= maxDepth) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    CollectFolders(dir, maxDepth, currentDepth + 1, result, ct);
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool ProcessFile(string filePath, DatabaseService db)
    {
        try
        {
            if (SupportedFormats.IsVideoFile(filePath)) return false;

            var fi = new FileInfo(filePath);
            if (!fi.Exists) return false;

            var existing = db.GetThumbnail(filePath, fi.LastWriteTime);
            if (existing is not null) return true;

            string? contentHash = null;
            try { contentHash = ContentHashService.ComputeHash(filePath, fi.Length); } catch { }

            if (contentHash is not null)
            {
                var hashHit = db.GetThumbnailByHash(contentHash);
                if (hashHit is not null)
                {
                    db.SaveThumbnail(filePath, fi.LastWriteTime, fi.Length,
                        hashHit.Value.Data, hashHit.Value.Width, hashHit.Value.Height, contentHash);
                    return true;
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

            if (thumbnailData.Length == 0) return false;

            db.SaveThumbnail(filePath, fi.LastWriteTime, fi.Length,
                thumbnailData, width, height, contentHash);
            return false;
        }
        catch
        {
            return false;
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
}
