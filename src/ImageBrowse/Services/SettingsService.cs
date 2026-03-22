using System.Globalization;
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

    public bool ConfirmBeforeDelete
    {
        get => _db.GetSetting("confirm_before_delete", "true") == "true";
        set => _db.SetSetting("confirm_before_delete", value ? "true" : "false");
    }

    public string RegisteredExtensions
    {
        get => _db.GetSetting("registered_extensions", "");
        set => _db.SetSetting("registered_extensions", value);
    }

    public bool EnableAnimations
    {
        get => _db.GetSetting("enable_animations", "true") == "true";
        set => _db.SetSetting("enable_animations", value ? "true" : "false");
    }

    public bool BossModeEnabled
    {
        get => _db.GetSetting("boss_mode_enabled", "true") == "true";
        set => _db.SetSetting("boss_mode_enabled", value ? "true" : "false");
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _db.GetSetting("check_updates_on_startup", "true") == "true";
        set => _db.SetSetting("check_updates_on_startup", value ? "true" : "false");
    }

    public double WindowLeft
    {
        get => double.TryParse(_db.GetSetting("window_left"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        set => _db.SetSetting("window_left", value.ToString(CultureInfo.InvariantCulture));
    }

    public double WindowTop
    {
        get => double.TryParse(_db.GetSetting("window_top"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        set => _db.SetSetting("window_top", value.ToString(CultureInfo.InvariantCulture));
    }

    public double WindowWidth
    {
        get => double.TryParse(_db.GetSetting("window_width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1400;
        set => _db.SetSetting("window_width", value.ToString(CultureInfo.InvariantCulture));
    }

    public double WindowHeight
    {
        get => double.TryParse(_db.GetSetting("window_height"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 900;
        set => _db.SetSetting("window_height", value.ToString(CultureInfo.InvariantCulture));
    }

    public int WindowState
    {
        get => int.TryParse(_db.GetSetting("window_state"), out var v) ? v : 0;
        set => _db.SetSetting("window_state", value.ToString());
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
