using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ImageBrowse.Services;

public static class FileOperationService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;

    /// <summary>Moves a file or folder to the OS trash / recycle bin where supported.</summary>
    public static bool MoveToRecycleBin(string path)
    {
        if (OperatingSystem.IsWindows())
            return MoveToRecycleBinWindows(path);
        if (OperatingSystem.IsMacOS())
            return MoveToTrashMac(path);
        if (OperatingSystem.IsLinux())
            return MoveToTrashLinux(path);

        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool MoveToRecycleBinWindows(string path)
    {
        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
        };

        int result = SHFileOperation(ref fileOp);
        return result == 0 && !fileOp.fAnyOperationsAborted;
    }

    private static bool MoveToTrashMac(string path)
    {
        try
        {
            string esc = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            p.StartInfo.ArgumentList.Add("-e");
            p.StartInfo.ArgumentList.Add($"tell application \"Finder\" to delete POSIX file \"{esc}\"");
            p.Start();
            p.WaitForExit(60000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool MoveToTrashLinux(string path)
    {
        try
        {
            if (RunProcessArgs("gio", "trash", path)) return true;
            if (RunProcessArgs("trash-put", path)) return true;
        }
        catch { }

        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RunProcessArgs(string fileName, params string[] args)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var a in args)
                p.StartInfo.ArgumentList.Add(a);
            p.Start();
            p.WaitForExit(60000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string? Error) Rename(string oldPath, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return (false, "Name cannot be empty.");

        char[] invalid = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalid) >= 0)
            return (false, "Name contains invalid characters.");

        string? parentDir = Path.GetDirectoryName(oldPath);
        if (parentDir is null)
            return (false, "Cannot determine parent directory.");

        string newPath = Path.Combine(parentDir, newName);

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return (true, null);

        if (File.Exists(newPath) || Directory.Exists(newPath))
            return (false, $"An item named \"{newName}\" already exists.");

        try
        {
            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
            else if (Directory.Exists(oldPath))
                Directory.Move(oldPath, newPath);
            else
                return (false, "Source item not found.");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static bool MoveItems(string[] sources, string destinationFolder, IntPtr ownerHwnd = default)
    {
        if (sources.Length == 0) return false;

        if (OperatingSystem.IsWindows())
            return MoveItemsWindows(sources, destinationFolder, ownerHwnd);

        try
        {
            foreach (var src in sources)
            {
                string name = Path.GetFileName(src);
                string dest = Path.Combine(destinationFolder, name);
                if (File.Exists(src))
                    File.Move(src, dest, overwrite: false);
                else if (Directory.Exists(src))
                    Directory.Move(src, dest);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool MoveItemsWindows(string[] sources, string destinationFolder, IntPtr ownerHwnd)
    {
        string pFrom = string.Join('\0', sources) + '\0' + '\0';
        string pTo = destinationFolder + '\0' + '\0';

        var fileOp = new SHFILEOPSTRUCT
        {
            hwnd = ownerHwnd,
            wFunc = FO_MOVE,
            pFrom = pFrom,
            pTo = pTo,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR
        };

        int result = SHFileOperation(ref fileOp);
        return result == 0 && !fileOp.fAnyOperationsAborted;
    }

    public static bool CopyItems(string[] sources, string destinationFolder, IntPtr ownerHwnd = default)
    {
        if (sources.Length == 0) return false;

        if (OperatingSystem.IsWindows())
            return CopyItemsWindows(sources, destinationFolder, ownerHwnd);

        try
        {
            foreach (var src in sources)
            {
                string name = Path.GetFileName(src);
                string dest = Path.Combine(destinationFolder, name);
                if (File.Exists(src))
                    File.Copy(src, dest, overwrite: false);
                else if (Directory.Exists(src))
                    CopyDirectoryRecursive(src, dest);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    [SupportedOSPlatform("windows")]
    private static bool CopyItemsWindows(string[] sources, string destinationFolder, IntPtr ownerHwnd)
    {
        string pFrom = string.Join('\0', sources) + '\0' + '\0';
        string pTo = destinationFolder + '\0' + '\0';

        var fileOp = new SHFILEOPSTRUCT
        {
            hwnd = ownerHwnd,
            wFunc = FO_COPY,
            pFrom = pFrom,
            pTo = pTo,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR
        };

        int result = SHFileOperation(ref fileOp);
        return result == 0 && !fileOp.fAnyOperationsAborted;
    }

    public static bool CreateFolder(string parentPath, string folderName)
    {
        try
        {
            string newPath = Path.Combine(parentPath, folderName);
            if (Directory.Exists(newPath)) return false;
            Directory.CreateDirectory(newPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetNewFolderPath(string parentDir)
    {
        string baseName = "New Folder";
        string path = Path.Combine(parentDir, baseName);
        if (!Directory.Exists(path)) return path;

        for (int i = 2; i < 1000; i++)
        {
            path = Path.Combine(parentDir, $"{baseName} ({i})");
            if (!Directory.Exists(path)) return path;
        }
        return path;
    }
}
