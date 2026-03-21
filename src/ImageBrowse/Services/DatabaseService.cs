using System.IO;
using Microsoft.Data.Sqlite;

namespace ImageBrowse.Services;

public sealed class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ImageBrowse", "cache.db");

    public DatabaseService()
    {
        var dir = Path.GetDirectoryName(DbPath)!;
        Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={DbPath}");
        _connection.Open();

        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA cache_size=-64000;

            CREATE TABLE IF NOT EXISTS thumbnails (
                file_path TEXT PRIMARY KEY,
                last_modified TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                thumbnail BLOB NOT NULL,
                width INTEGER DEFAULT 0,
                height INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS metadata (
                file_path TEXT PRIMARY KEY,
                last_modified TEXT NOT NULL,
                date_taken TEXT,
                image_width INTEGER DEFAULT 0,
                image_height INTEGER DEFAULT 0,
                camera_make TEXT,
                camera_model TEXT,
                lens_model TEXT,
                iso INTEGER,
                f_number TEXT,
                exposure_time TEXT,
                focal_length TEXT
            );

            CREATE TABLE IF NOT EXISTS ratings (
                file_path TEXT PRIMARY KEY,
                rating INTEGER NOT NULL DEFAULT 0,
                is_tagged INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                parent_id INTEGER,
                FOREIGN KEY (parent_id) REFERENCES categories(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS file_categories (
                file_path TEXT NOT NULL,
                category_id INTEGER NOT NULL,
                PRIMARY KEY (file_path, category_id),
                FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS folder_sort_preferences (
                folder_path TEXT PRIMARY KEY,
                sort_field INTEGER NOT NULL,
                sort_direction INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_thumbnails_modified ON thumbnails(last_modified);
            CREATE INDEX IF NOT EXISTS idx_metadata_modified ON metadata(last_modified);
            """;
        cmd.ExecuteNonQuery();

        MigrateSchema();
    }

    private void MigrateSchema()
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE thumbnails ADD COLUMN content_hash TEXT";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists
        }

        using var idx = _connection.CreateCommand();
        idx.CommandText = "CREATE INDEX IF NOT EXISTS idx_thumbnails_content_hash ON thumbnails(content_hash)";
        idx.ExecuteNonQuery();
    }

    public byte[]? GetThumbnail(string filePath, DateTime lastModified)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT thumbnail FROM thumbnails WHERE file_path = $path AND last_modified = $modified";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$modified", lastModified.ToString("O"));
        return cmd.ExecuteScalar() as byte[];
    }

    public void SaveThumbnail(string filePath, DateTime lastModified, long fileSize, byte[] thumbnailData, int width, int height, string? contentHash = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO thumbnails (file_path, last_modified, file_size, thumbnail, width, height, content_hash)
            VALUES ($path, $modified, $size, $data, $w, $h, $hash)
            """;
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$modified", lastModified.ToString("O"));
        cmd.Parameters.AddWithValue("$size", fileSize);
        cmd.Parameters.AddWithValue("$data", thumbnailData);
        cmd.Parameters.AddWithValue("$w", width);
        cmd.Parameters.AddWithValue("$h", height);
        cmd.Parameters.AddWithValue("$hash", (object?)contentHash ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public (byte[] Data, int Width, int Height)? GetThumbnailByHash(string contentHash)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT thumbnail, width, height FROM thumbnails WHERE content_hash = $hash LIMIT 1";
        cmd.Parameters.AddWithValue("$hash", contentHash);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var data = (byte[])reader["thumbnail"];
            int w = reader.GetInt32(1);
            int h = reader.GetInt32(2);
            return (data, w, h);
        }
        return null;
    }

    public (int Width, int Height)? GetCachedDimensions(string filePath, DateTime lastModified)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT width, height FROM thumbnails WHERE file_path = $path AND last_modified = $modified";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$modified", lastModified.ToString("O"));
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            int w = reader.GetInt32(0);
            int h = reader.GetInt32(1);
            if (w > 0 && h > 0) return (w, h);
        }
        return null;
    }

    public void SaveMetadata(string filePath, DateTime lastModified,
        DateTime? dateTaken, int width, int height,
        string? cameraMake, string? cameraModel, string? lensModel,
        int? iso, string? fNumber, string? exposureTime, string? focalLength)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO metadata
            (file_path, last_modified, date_taken, image_width, image_height,
             camera_make, camera_model, lens_model, iso, f_number, exposure_time, focal_length)
            VALUES ($path, $modified, $taken, $w, $h, $make, $model, $lens, $iso, $fn, $exp, $fl)
            """;
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$modified", lastModified.ToString("O"));
        cmd.Parameters.AddWithValue("$taken", dateTaken?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$w", width);
        cmd.Parameters.AddWithValue("$h", height);
        cmd.Parameters.AddWithValue("$make", (object?)cameraMake ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)cameraModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lens", (object?)lensModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iso", (object?)iso ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fn", (object?)fNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exp", (object?)exposureTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fl", (object?)focalLength ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public int GetRating(string filePath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT rating FROM ratings WHERE file_path = $path";
        cmd.Parameters.AddWithValue("$path", filePath);
        var result = cmd.ExecuteScalar();
        return result is long r ? (int)r : 0;
    }

    public bool GetTagged(string filePath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT is_tagged FROM ratings WHERE file_path = $path";
        cmd.Parameters.AddWithValue("$path", filePath);
        var result = cmd.ExecuteScalar();
        return result is long t && t != 0;
    }

    public void SetRating(string filePath, int rating)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ratings (file_path, rating, is_tagged) VALUES ($path, $rating, 0)
            ON CONFLICT(file_path) DO UPDATE SET rating = $rating
            """;
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$rating", rating);
        cmd.ExecuteNonQuery();
    }

    public void SetTagged(string filePath, bool tagged)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ratings (file_path, rating, is_tagged) VALUES ($path, 0, $tagged)
            ON CONFLICT(file_path) DO UPDATE SET is_tagged = $tagged
            """;
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$tagged", tagged ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public string? GetSetting(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public string GetSetting(string key, string defaultValue)
    {
        return GetSetting(key) ?? defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = $value
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    public (int SortField, int SortDirection)? GetFolderSortPreference(string folderPath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT sort_field, sort_direction FROM folder_sort_preferences WHERE folder_path = $path";
        cmd.Parameters.AddWithValue("$path", folderPath);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return (reader.GetInt32(0), reader.GetInt32(1));
        return null;
    }

    public void SetFolderSortPreference(string folderPath, int sortField, int sortDirection)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO folder_sort_preferences (folder_path, sort_field, sort_direction) VALUES ($path, $field, $dir)
            ON CONFLICT(folder_path) DO UPDATE SET sort_field = $field, sort_direction = $dir
            """;
        cmd.Parameters.AddWithValue("$path", folderPath);
        cmd.Parameters.AddWithValue("$field", sortField);
        cmd.Parameters.AddWithValue("$dir", sortDirection);
        cmd.ExecuteNonQuery();
    }

    public void ClearFolderSortPreference(string folderPath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM folder_sort_preferences WHERE folder_path = $path";
        cmd.Parameters.AddWithValue("$path", folderPath);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
