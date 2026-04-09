using System.Runtime.InteropServices;
using Microsoft.Win32;
using ImageBrowse;

namespace ImageBrowse.Services;

public static partial class FileAssociationService
{
    private const string ProgId = "ImageBrowse.Image";
    private static string AppName => AppBranding.DisplayName;
    private const string AppDescription = "A lightweight, fast image browser for Windows";
    private const string CapabilitiesPath = @"Software\ImageBrowse\Capabilities";
    private const string DirShellKey = @"Software\Classes\Directory\shell\BrowseWithImageBrowse";
    private const string DirBgShellKey = @"Software\Classes\Directory\Background\shell\BrowseWithImageBrowse";
    private const string ContextMenuLabel = "Browse with Image Viewer";

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static string GetExecutablePath() =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine executable path.");

    public static void RegisterFileAssociations(IEnumerable<string> extensions)
    {
        var exePath = GetExecutablePath();
        var extList = extensions.ToList();
        if (extList.Count == 0) return;

        RegisterProgId(exePath);
        RegisterCapabilities(exePath, extList);

        foreach (var ext in extList)
        {
            var normalized = NormalizeExtension(ext);
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{normalized}\OpenWithProgids");
            key.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyShell();
    }

    public static void UnregisterFileAssociations(IEnumerable<string> extensions)
    {
        foreach (var ext in extensions)
        {
            var normalized = NormalizeExtension(ext);
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{normalized}\OpenWithProgids", writable: true);
                key?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
            catch { }

            try
            {
                using var capKey = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesPath}\FileAssociations", writable: true);
                capKey?.DeleteValue(normalized, throwOnMissingValue: false);
            }
            catch { }
        }

        NotifyShell();
    }

    public static void UnregisterAll()
    {
        var registered = GetRegisteredExtensions();
        UnregisterFileAssociations(registered);

        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ImageBrowse", throwOnMissingSubKey: false); } catch { }

        try
        {
            using var regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
            regApps?.DeleteValue("ImageBrowse", throwOnMissingValue: false);
        }
        catch { }

        UnregisterContextMenu();
        NotifyShell();
    }

    public static HashSet<string> GetRegisteredExtensions()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var capKey = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesPath}\FileAssociations");
            if (capKey is null) return result;

            foreach (var name in capKey.GetValueNames())
            {
                if (capKey.GetValue(name) is string val && val == ProgId)
                    result.Add(name);
            }
        }
        catch { }

        return result;
    }

    public static void RegisterContextMenu()
    {
        var exePath = GetExecutablePath();
        var quotedExe = $"\"{exePath}\"";

        using (var key = Registry.CurrentUser.CreateSubKey(DirShellKey))
        {
            key.SetValue(null, ContextMenuLabel);
            key.SetValue("Icon", exePath);
        }
        using (var cmd = Registry.CurrentUser.CreateSubKey($@"{DirShellKey}\command"))
        {
            cmd.SetValue(null, $"{quotedExe} \"%V\"");
        }

        using (var key = Registry.CurrentUser.CreateSubKey(DirBgShellKey))
        {
            key.SetValue(null, ContextMenuLabel);
            key.SetValue("Icon", exePath);
        }
        using (var cmd = Registry.CurrentUser.CreateSubKey($@"{DirBgShellKey}\command"))
        {
            cmd.SetValue(null, $"{quotedExe} \"%V\"");
        }
    }

    public static void UnregisterContextMenu()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(DirShellKey, throwOnMissingSubKey: false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(DirBgShellKey, throwOnMissingSubKey: false); } catch { }
    }

    public static bool IsContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DirShellKey);
            return key is not null;
        }
        catch { return false; }
    }

    private static void RegisterProgId(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
        key.SetValue(null, AppName);

        using var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon");
        iconKey.SetValue(null, $"\"{exePath}\",0");

        using var cmdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        cmdKey.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    private static void RegisterCapabilities(string exePath, List<string> extensions)
    {
        using var capKey = Registry.CurrentUser.CreateSubKey(CapabilitiesPath);
        capKey.SetValue("ApplicationName", AppName);
        capKey.SetValue("ApplicationDescription", AppDescription);
        capKey.SetValue("ApplicationIcon", $"\"{exePath}\",0");

        using var assocKey = Registry.CurrentUser.CreateSubKey($@"{CapabilitiesPath}\FileAssociations");
        foreach (var ext in extensions)
        {
            assocKey.SetValue(NormalizeExtension(ext), ProgId);
        }

        using var regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        regApps.SetValue("ImageBrowse", CapabilitiesPath);
    }

    private static string NormalizeExtension(string ext) =>
        ext.StartsWith('.') ? ext.ToLowerInvariant() : $".{ext.ToLowerInvariant()}";

    private static void NotifyShell() =>
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
}
