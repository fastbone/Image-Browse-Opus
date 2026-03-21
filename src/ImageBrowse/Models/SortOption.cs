namespace ImageBrowse.Models;

public enum SortField
{
    FileName,
    DateModified,
    DateCreated,
    DateTaken,
    FileSize,
    Dimensions,
    FileType,
    Rating
}

public enum SortDirection
{
    Ascending,
    Descending
}

public record SortOption(SortField Field, SortDirection Direction)
{
    public string DisplayName => Field switch
    {
        SortField.FileName => "Name",
        SortField.DateModified => "Date Modified",
        SortField.DateCreated => "Date Created",
        SortField.DateTaken => "Date Taken",
        SortField.FileSize => "File Size",
        SortField.Dimensions => "Dimensions",
        SortField.FileType => "File Type",
        SortField.Rating => "Rating",
        _ => Field.ToString()
    };
}
