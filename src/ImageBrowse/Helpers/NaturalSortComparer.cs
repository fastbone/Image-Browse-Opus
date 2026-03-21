using System.Runtime.InteropServices;

namespace ImageBrowse.Helpers;

public sealed partial class NaturalSortComparer : IComparer<string>
{
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return StrCmpLogicalW(x, y);
    }

    public static NaturalSortComparer Instance { get; } = new();
}
