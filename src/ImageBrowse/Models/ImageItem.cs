using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media.Imaging;

namespace ImageBrowse.Models;

public partial class ImageItem : ObservableObject
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required long FileSize { get; init; }
    public required DateTime DateModified { get; init; }
    public required DateTime DateCreated { get; init; }

    [ObservableProperty] private BitmapSource? _thumbnail;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private int _rating;
    [ObservableProperty] private bool _isTagged;
    [ObservableProperty] private bool _isThumbnailLoading;

    public DateTime? DateTaken { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string? CameraModel { get; set; }
    public string? CameraManufacturer { get; set; }
    public string? LensModel { get; set; }
    public int? Iso { get; set; }
    public string? FNumber { get; set; }
    public string? ExposureTime { get; set; }
    public string? FocalLength { get; set; }
    public bool MetadataLoaded { get; set; }

    public bool IsFolder { get; init; }
    public int FolderImageCount { get; set; }
    public int FolderSubfolderCount { get; set; }

    public string DimensionsDisplay =>
        ImageWidth > 0 && ImageHeight > 0 ? $"{ImageWidth} × {ImageHeight}" : "";

    public string FileSizeDisplay => FormatFileSize(FileSize);

    public string SubtitleDisplay => IsFolder
        ? FormatFolderSubtitle()
        : $"{DimensionsDisplay}  {FileSizeDisplay}";

    private string FormatFolderSubtitle()
    {
        var parts = new List<string>();
        if (FolderImageCount > 0)
            parts.Add($"{FolderImageCount} image{(FolderImageCount != 1 ? "s" : "")}");
        if (FolderSubfolderCount > 0)
            parts.Add($"{FolderSubfolderCount} folder{(FolderSubfolderCount != 1 ? "s" : "")}");
        return parts.Count > 0 ? string.Join(", ", parts) : "Empty";
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
