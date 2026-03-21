using ImageBrowse.Models;

namespace ImageBrowse.Services;

public sealed class SettingsService
{
    private readonly DatabaseService _db;

    public SettingsService(DatabaseService db)
    {
        _db = db;
    }

    public string StartupFolder
    {
        get => _db.GetSetting("startup_folder",
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        set => _db.SetSetting("startup_folder", value);
    }

    public SortField DefaultSortField
    {
        get => Enum.TryParse<SortField>(_db.GetSetting("default_sort_field"), out var f) ? f : SortField.FileName;
        set => _db.SetSetting("default_sort_field", ((int)value).ToString());
    }

    public SortDirection DefaultSortDirection
    {
        get => Enum.TryParse<SortDirection>(_db.GetSetting("default_sort_direction"), out var d) ? d : SortDirection.Ascending;
        set => _db.SetSetting("default_sort_direction", ((int)value).ToString());
    }

    public bool IsDarkTheme
    {
        get => _db.GetSetting("dark_theme", "true") == "true";
        set => _db.SetSetting("dark_theme", value ? "true" : "false");
    }

    public int ThumbnailSize
    {
        get => int.TryParse(_db.GetSetting("thumbnail_size"), out var s) ? s : 180;
        set => _db.SetSetting("thumbnail_size", value.ToString());
    }

    public bool IsFolderTreeVisible
    {
        get => _db.GetSetting("folder_tree_visible", "true") == "true";
        set => _db.SetSetting("folder_tree_visible", value ? "true" : "false");
    }

    public (SortField Field, SortDirection Direction)? GetFolderSort(string folderPath)
    {
        var pref = _db.GetFolderSortPreference(folderPath);
        if (pref is null) return null;
        return ((SortField)pref.Value.SortField, (SortDirection)pref.Value.SortDirection);
    }

    public void SetFolderSort(string folderPath, SortField field, SortDirection direction)
    {
        _db.SetFolderSortPreference(folderPath, (int)field, (int)direction);
    }

    public void ClearFolderSort(string folderPath)
    {
        _db.ClearFolderSortPreference(folderPath);
    }
}
