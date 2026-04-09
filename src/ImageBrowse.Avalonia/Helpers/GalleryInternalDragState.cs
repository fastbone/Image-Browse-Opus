using ImageBrowse.Models;

namespace ImageBrowse.Helpers;

/// <summary>Holds in-memory gallery selection during an internal drag so tree drop can call OnItemsMoved without a full rescan.</summary>
internal static class GalleryInternalDragState
{
    internal static IReadOnlyList<ImageItem>? Items;
}
