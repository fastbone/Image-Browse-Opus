# Image Browse

A lightweight, fast image and video browser for Windows built with .NET 10 and WPF.

[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![GitHub release](https://img.shields.io/github/v/release/fastbone/Image-Browse-Opus)](https://github.com/fastbone/Image-Browse-Opus/releases)

---

## Overview

Image Browse is a Windows desktop application for browsing and viewing images and videos. It combines a familiar folder-based navigation experience with a high-performance virtualized thumbnail gallery and a full-featured media viewer. Powered by ImageMagick through Magick.NET and LibVLC for video playback, it supports over 80 image formats and 16 video formats out of the box -- from everyday JPEGs and PNGs to camera RAW files, HEIC, AVIF, JPEG XL, PSD, SVG, HDR, and many more, plus video files like MP4, MKV, and MOV.

---

## Features

### Folder Navigation
- **Folder tree** with drives and special folders (Pictures, Desktop, Documents, Downloads)
- **Breadcrumb navigation bar** with clickable path segments and direct path editing (`Ctrl+L`)
- **Back / Forward / Up** navigation with full history

### Thumbnail Gallery
- **Virtualized wrap panel** for smooth scrolling through thousands of items
- **Adjustable thumbnail size** from 80px to 400px (step of 40px)
- Mixed display of folders and images; folder tiles show image counts and composite previews
- **Extended selection** support

### Image Viewing
- **Fullscreen viewer** with black background for distraction-free viewing
- **Zoom and pan** with mouse wheel and keyboard controls
- **Fit to screen**, **actual size**, and arbitrary zoom levels
- **EXIF info overlay** showing camera make, model, lens, ISO, aperture, shutter speed, focal length, and dimensions
- **Filmstrip** thumbnail strip for quick navigation between images (toggle pin with `T`)
- Auto-hiding cursor and position/zoom indicators

### Video Playback
- **Integrated video player** powered by LibVLC for broad codec support
- **Playback controls**: play/pause, seek (5s or 30s jumps), volume, mute
- **Playback speed adjustment** with `[` and `]` keys (0.25x to 4x)
- **Video zoom** with mini-map for interactive navigation
- **Video thumbnails** generated automatically from video content
- Supported formats: MP4, MKV, AVI, MOV, WebM, WMV, FLV, M4V, MPG, MPEG, TS, 3GP, OGV, VOB, MTS, M2TS

### Organization
- **Star ratings** (1-5) per image, stored persistently
- **Tagging** to mark images of interest
- **Per-folder sort preferences** that persist across sessions
- **Sort by**: file name (natural sort), date modified, date created, date taken, file size, dimensions, file type, or rating

### Image Operations
- **Rotate 90 degrees clockwise** with automatic EXIF orientation handling
- Lossless rotation for JPEG files (quality 100)
- **Delete images** from the gallery or fullscreen viewer (with optional confirmation)
- Thumbnail cache automatically refreshed after rotation

### Performance
- **SQLite-backed thumbnail cache** for instant re-visits
- **Content-hash deduplication** -- identical images share cached thumbnails
- **Folder prescan** to warm the thumbnail cache for entire directory trees (configurable depth: current folder, 1, 2, 5 levels, or unlimited)
- WPF-native fast path for common formats (JPEG, PNG, BMP, GIF, TIFF, ICO) with Magick.NET fallback

### Appearance
- **Dark theme** and **Light theme** with full UI coverage
- **Transition animations** for dialogs, loading states, and UI elements (can be disabled in settings)
- **Segmented sort controls** in the toolbar for quick sorting
- **Shimmer loading placeholders** for smooth thumbnail loading feedback
- Theme preference persisted across sessions

### Updates
- **Automatic update checking** via Velopack and GitHub Releases
- Manual check available from the About dialog

---

## Supported Formats

Image Browse supports over 80 image formats and 16 video formats. Commonly used image formats are loaded through WPF's native decoders for maximum speed; other image formats go through ImageMagick. Video playback is handled by LibVLC.

### Common Raster
`.jpg` `.jpeg` `.jfif` `.png` `.gif` `.bmp` `.tiff` `.tif` `.ico` `.cur`

### Modern / Web
`.webp` `.heic` `.heif` `.avif` `.jxl` `.apng` `.mng` `.svg` `.wbmp`

### Camera RAW
`.cr2` `.cr3` `.crw` `.nef` `.nrw` `.arw` `.sr2` `.srf` `.orf` `.raf` `.rw2` `.rwl` `.pef` `.dng` `.mrw` `.x3f` `.srw` `.3fr` `.dcr` `.kdc` `.erf` `.mos` `.mef` `.raw` `.bay` `.cap` `.iiq` `.ptx`

### Design / Layered
`.psd` `.psb` `.xcf` `.ai` `.eps`

### HDR / Scientific
`.hdr` `.exr` `.rgbe` `.pfm` `.fits` `.fit` `.fts` `.dpx` `.cin`

### JPEG 2000
`.jp2` `.j2k` `.jpf` `.jpm` `.jpg2`

### Legacy / Other
`.tga` `.dds` `.pcx` `.pbm` `.pgm` `.ppm` `.pnm` `.sgi` `.xbm` `.xpm` `.wmf` `.emf`

### Video
`.mp4` `.mkv` `.avi` `.mov` `.webm` `.wmv` `.flv` `.m4v` `.mpg` `.mpeg` `.ts` `.3gp` `.ogv` `.vob` `.mts` `.m2ts`

---

## Keyboard Shortcuts

### Main Window

| Shortcut | Action |
|---|---|
| `Enter` / `F11` / `F` | Enter fullscreen viewer |
| `Escape` (press twice within 2s) | Quit application |
| `Ctrl+T` | Toggle dark/light theme |
| `Ctrl+Shift+F` | Toggle folder tree visibility |
| `Ctrl+Plus` / `Ctrl+NumPad+` | Increase thumbnail size |
| `Ctrl+Minus` / `Ctrl+NumPad-` | Decrease thumbnail size |
| `Alt+Left` | Navigate back |
| `Alt+Right` | Navigate forward |
| `Alt+Up` | Navigate to parent folder |
| `Ctrl+,` | Open Settings |
| `Ctrl+Shift+P` | Open Prescan dialog |
| `Ctrl+L` | Focus address bar for path editing |
| `F1` | Open About dialog |

### Gallery View

| Shortcut | Action |
|---|---|
| `Enter` / `F` | Open folder or enter fullscreen for selected image |
| `Backspace` / `BrowserBack` | Navigate to parent folder |
| `1`-`5` / `NumPad1`-`NumPad5` | Set rating (press same rating again to clear) |
| `R` | Rotate selected image 90 degrees clockwise |
| `Q` | Toggle tag on selected image |
| `Home` | Jump to first image |
| `End` | Jump to last image |
| `Delete` | Delete selected image(s) |

### Fullscreen Viewer (Image)

| Shortcut | Action |
|---|---|
| `Escape` / `Enter` | Exit fullscreen |
| `Right` / `Space` / `PageDown` | Next image |
| `Left` / `Backspace` / `PageUp` | Previous image |
| `Home` | First image |
| `End` | Last image |
| `Plus` / `NumPad+` | Zoom in |
| `Minus` / `NumPad-` | Zoom out |
| `0` / `NumPad0` | Fit to screen |
| `Ctrl+1` | Actual size (1:1 pixel mapping) |
| `I` | Toggle EXIF info overlay |
| `T` | Toggle filmstrip pin |
| `R` | Rotate image 90 degrees clockwise |
| `Q` | Toggle tag |
| `Delete` | Delete current image |
| `1`-`5` / `NumPad1`-`NumPad5` | Set rating |

### Fullscreen Viewer (Video)

When a video is playing, these shortcuts apply instead:

| Shortcut | Action |
|---|---|
| `Escape` / `Enter` | Exit fullscreen |
| `Space` | Toggle play/pause |
| `Left` | Seek backward 5 seconds |
| `Shift+Left` | Seek backward 30 seconds |
| `Right` | Seek forward 5 seconds |
| `Shift+Right` | Seek forward 30 seconds |
| `Up` | Volume up |
| `Down` | Volume down |
| `M` | Toggle mute |
| `Z` | Toggle video zoom |
| `N` | Next file |
| `P` | Previous file |
| `[` | Decrease playback speed |
| `]` | Increase playback speed |
| `1`-`5` / `NumPad1`-`NumPad5` | Set rating |

### Fullscreen Mouse Controls

| Input | Action |
|---|---|
| Mouse wheel | Zoom in/out (images) |
| XButton1 (back) | Previous image |
| XButton2 (forward) | Next image |

---

## System Requirements

- **OS**: Windows 10 or Windows 11 (64-bit), version 1809 (build 17763) or later
- **Runtime**: .NET 10.0 Desktop Runtime ([download](https://dotnet.microsoft.com/download/dotnet/10.0))

---

## Installation

### From GitHub Releases

1. Go to the [Releases](https://github.com/fastbone/Image-Browse-Opus/releases) page
2. Download the latest release
3. Run the installer or extract the portable archive
4. Launch `ImageBrowse.exe`

When installed via Velopack, Image Browse checks for updates automatically on startup and can be updated from the About dialog.

### Building from Source

#### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Visual Studio 2026](https://visualstudio.microsoft.com/) or later with the **.NET desktop development** workload, or any editor with `dotnet` CLI access

#### Clone and Build

```bash
git clone https://github.com/fastbone/Image-Browse-Opus.git
cd Image-Browse-Opus
dotnet restore src/ImageBrowse/ImageBrowse.csproj
dotnet build src/ImageBrowse/ImageBrowse.csproj -c Release
```

#### Run

```bash
dotnet run --project src/ImageBrowse/ImageBrowse.csproj -c Release
```

The compiled output is placed in `src/ImageBrowse/bin/Release/net10.0-windows/`.

---

## Configuration

### Settings Dialog (`Ctrl+,`)

| Setting | Description |
|---|---|
| Startup folder | The folder opened when the application starts (defaults to My Pictures) |
| Default sort | The sort field and direction applied to folders without a custom sort preference |
| Theme | Switch between dark and light themes |
| Enable animations | Toggle transition animations for dialogs, loading states, and UI elements |
| Confirm before delete | Show a confirmation dialog before deleting files |
| Clear thumbnail cache | Removes all cached thumbnails from the database |

### Thumbnail Size

Adjust from the main toolbar or with `Ctrl+Plus` / `Ctrl+Minus`. Range: 80px to 400px in 40px increments. The setting persists across sessions.

### Per-Folder Sort

Right-click or use the sort dropdown in the toolbar to set a custom sort for the current folder. Each folder's preference is stored independently in the database and overrides the default sort. Clear a folder's custom sort to revert to the default.

### Data Location

All cached data (thumbnails, metadata, ratings, tags, settings, sort preferences) is stored in a single SQLite database at:

```
%LocalApplicationData%\ImageBrowse\cache.db
```

---

## Architecture

Image Browse follows the **MVVM** (Model-View-ViewModel) pattern:

```
src/ImageBrowse/
  Program.cs              Application entry point (Velopack bootstrap)
  App.xaml                Application definition and startup theme
  MainWindow.xaml         Main window shell (toolbar, tree, gallery)
  Views/
    GalleryView.xaml      Thumbnail grid with virtualized wrap panel
    FullscreenViewer.xaml  Fullscreen image viewer with zoom/info overlay
    SettingsDialog.xaml    Application settings
    PrescanDialog.xaml     Thumbnail prescan progress
    AboutDialog.xaml       Version, credits, and update check
  ViewModels/
    MainViewModel.cs      Core application state and commands
  Models/
    ImageItem.cs          Represents an image or folder in the gallery
    SortOption.cs         Sort field and direction
  Services/
    DatabaseService.cs    SQLite database access (cache, settings, ratings)
    ThumbnailService.cs   Thumbnail generation and caching
    FolderThumbnailService.cs  Composite folder preview generation
    VideoThumbnailService.cs   Video thumbnail extraction via LibVLC
    ImageLoadingService.cs     Full image loading (WPF native + Magick.NET)
    MetadataService.cs    EXIF/metadata extraction via MetadataExtractor
    ExifOrientationService.cs  EXIF orientation correction
    ImageRotationService.cs    Lossless 90-degree rotation
    SettingsService.cs    Persistent settings management
    PrescanService.cs     Background folder tree thumbnail warming
    UpdateService.cs      Velopack auto-update integration
    ContentHashService.cs Content-based thumbnail deduplication
  Helpers/
    Converters.cs         WPF value converters
    NaturalSortComparer.cs  Natural string sorting (e.g. "img2" before "img10")
    DialogAnimationHelper.cs  Slide/fade dialog animations
    RatingStarsControl.cs     Interactive star rating control
  Themes/
    DarkTheme.xaml        Dark color scheme and control styles
    LightTheme.xaml       Light color scheme and control styles
  Resources/
    app.ico               Application icon
```

---

## Third-Party Libraries

Image Browse is built on the following open-source libraries. All use licenses compatible with GPL-3.0.

| Library | Version | License | Project |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.1 | MIT | [GitHub](https://github.com/CommunityToolkit/dotnet) |
| LibVLCSharp | 3.9.6 | LGPL-2.1+ | [GitHub](https://github.com/videolan/libvlcsharp) |
| LibVLCSharp.WPF | 3.9.6 | LGPL-2.1+ | [GitHub](https://github.com/videolan/libvlcsharp) |
| VideoLAN.LibVLC.Windows | 3.0.23 | LGPL-2.1+ / GPL-2.0+ | [GitHub](https://github.com/videolan/vlc) |
| Magick.NET-Q8-AnyCPU | 14.11.0 | Apache-2.0 | [GitHub](https://github.com/dlemstra/Magick.NET) |
| MetadataExtractor | 2.9.2 | Apache-2.0 | [GitHub](https://github.com/drewnoakes/metadata-extractor-dotnet) |
| Microsoft.Data.Sqlite | 10.0.5 | Apache-2.0 | [GitHub](https://github.com/dotnet/efcore) |
| Velopack | 0.0.1298 | MIT | [GitHub](https://github.com/velopack/velopack) |
| VirtualizingWrapPanel | 2.5.1 | MIT | [GitHub](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) |
| XmpCore | 6.1.10.1 | BSD-3-Clause | [GitHub](https://github.com/drewnoakes/xmp-core-dotnet) |
| SQLitePCLRaw | 2.1.11 | Apache-2.0 | [GitHub](https://github.com/ericsink/SQLitePCL.raw) |
| NuGet.Versioning | 6.14.0 | Apache-2.0 | [GitHub](https://github.com/NuGet/NuGet.Client) |

For full license texts, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Contributing

Contributions are welcome. To get started:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Make your changes and ensure the project builds without errors
4. Commit your changes with a clear message
5. Push to your fork and open a Pull Request

Please keep pull requests focused on a single change. For large features or architectural changes, open an issue first to discuss the approach.

---

## License

Image Browse is licensed under the **GNU General Public License v3.0**.

Copyright (c) 2026 fastbone

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

See [LICENSE](LICENSE) for the full license text.
