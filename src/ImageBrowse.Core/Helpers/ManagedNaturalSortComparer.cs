namespace ImageBrowse.Helpers;

/// <summary>
/// Platform-independent natural sort comparer that handles embedded numeric segments.
/// Platforms with a native equivalent (e.g. StrCmpLogicalW on Windows) can substitute their own.
/// </summary>
public sealed class ManagedNaturalSortComparer : IComparer<string>
{
    public static ManagedNaturalSortComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            char cx = x[ix], cy = y[iy];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int zx = 0, zy = 0;
                while (ix + zx < x.Length && x[ix + zx] == '0') zx++;
                while (iy + zy < y.Length && y[iy + zy] == '0') zy++;

                int nx = ix + zx, ny = iy + zy;
                while (nx < x.Length && char.IsDigit(x[nx])) nx++;
                while (ny < y.Length && char.IsDigit(y[ny])) ny++;

                int lenX = nx - ix - zx;
                int lenY = ny - iy - zy;

                if (lenX != lenY) return lenX - lenY;

                for (int i = 0; i < lenX; i++)
                {
                    int cmp = x[ix + zx + i] - y[iy + zy + i];
                    if (cmp != 0) return cmp;
                }

                if (zx != zy) return zy - zx;

                ix = nx;
                iy = ny;
            }
            else
            {
                int cmp = char.ToUpperInvariant(cx) - char.ToUpperInvariant(cy);
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }

        return x.Length - y.Length;
    }
}
