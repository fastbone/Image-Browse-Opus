using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageBrowse.Models;

public partial class ImageItem : ObservableObject
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required long FileSize { get; set; }
    public required DateTime DateModified { get; set; }
    public required DateTime DateCreated { get; init; }

    [ObservableProperty] private object? _thumbnail;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private int _rating;
    [ObservableProperty] private bool _isTagged;
    [ObservableProperty] private bool _isThumbnailLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleDisplay))]
    [NotifyPropertyChangedFor(nameof(SortSubtitleDisplay))]
    private int _folderImageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortSubtitleDisplay))]
    private SortField _currentSortField = SortField.FileName;

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
    public bool IsParentFolder { get; init; }
    public bool IsVideo { get; init; }
    public int FolderSubfolderCount { get; set; }
    public TimeSpan? Duration { get; set; }

    public string DimensionsDisplay =>
        ImageWidth > 0 && ImageHeight > 0 ? $"{ImageWidth} × {ImageHeight}" : "";

    public string FileSizeDisplay => FormatFileSize(FileSize);

    public string DurationDisplay => Duration.HasValue
        ? Duration.Value.TotalHours >= 1
            ? Duration.Value.ToString(@"h\:mm\:ss")
            : Duration.Value.ToString(@"m\:ss")
        : "";

    public string SubtitleDisplay => IsParentFolder
        ? "Parent folder"
        : IsFolder
            ? FormatFolderSubtitle()
            : IsVideo
                ? FormatVideoSubtitle()
                : $"{DimensionsDisplay}  {FileSizeDisplay}";

    public string SortSubtitleDisplay => CurrentSortField switch
    {
        SortField.FileName => SubtitleDisplay,
        SortField.Dimensions => SubtitleDisplay,
        SortField.DateModified => IsParentFolder ? "Parent folder"
            : $"Modified: {DateModified:yyyy-MM-dd HH:mm}",
        SortField.DateCreated => IsParentFolder ? "Parent folder"
            : $"Created: {DateCreated:yyyy-MM-dd HH:mm}",
        SortField.DateTaken => IsParentFolder ? "Parent folder"
            : IsFolder ? FormatFolderSubtitle()
            : DateTaken.HasValue ? $"Taken: {DateTaken.Value:yyyy-MM-dd HH:mm}" : "Taken: \u2014",
        SortField.FileSize => IsParentFolder ? "Parent folder"
            : IsFolder ? FormatFolderSubtitle()
            : $"Size: {FileSizeDisplay}",
        SortField.FileType => IsParentFolder ? "Parent folder"
            : IsFolder ? FormatFolderSubtitle()
            : $"Type: {Extension}",
        SortField.Rating => IsParentFolder ? "Parent folder"
            : IsFolder ? FormatFolderSubtitle()
            : Rating > 0 ? $"Rating: {new string('\u2605', Rating)}{new string('\u2606', 5 - Rating)}" : "Rating: \u2014",
        _ => SubtitleDisplay
    };

    private string FormatVideoSubtitle()
    {
        var parts = new List<string>();
        if (Duration.HasValue) parts.Add(DurationDisplay);
        if (FileSize > 0) parts.Add(FileSizeDisplay);
        return parts.Count > 0 ? string.Join("  ", parts) : Extension;
    }

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
