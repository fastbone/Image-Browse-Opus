# Image Browse

A lightweight, fast desktop image and video browser built with .NET 10. **Windows** ships as a **WPF** app (full feature set); **macOS** and **Linux** use **Avalonia** with the same shared core.

[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![GitHub release](https://img.shields.io/github/v/release/fastbone/Image-Browse-Opus)](https://github.com/fastbone/Image-Browse-Opus/releases)

---

## Overview

Image Browse is a desktop application for browsing and viewing images and videos. It combines folder-based navigation, a high-performance thumbnail gallery (virtualized on Windows WPF; Avalonia uses a wrap layout with aggressive visible-range thumbnail loading), and a fullscreen media viewer. **Magick.NET** (ImageMagick) powers broad image format support on every platform. **LibVLC** adds integrated video playback and video thumbnails on **Windows** and **macOS** Avalonia builds; **Linux** Avalonia builds expect a system **libvlc** installation (plugins on `LD_LIBRARY_PATH` or distro packages) because a NuGet-native Linux transport is not bundled here. The app supports over 80 image formats and common video containers where LibVLC is available.

### Platform note

**Windows (WPF)** is the primary build: LibVLC video, virtualized gallery, **Velopack** auto-updates, and WPF-native fast decoding for common image types. **macOS** and **Linux** use **Avalonia** with the same **ImageBrowse.Core** (gallery, fullscreen, ratings, tags, SQLite cache, prescan, file operations, breadcrumbs, startup paths, update checks, LibVLC on macOS and when `libvlc` is present on Linux). Optional Linux/macOS file associations are documented under [`docs/unix-file-associations.md`](docs/unix-file-associations.md).

---

## Platforms and downloads

Official builds are published on [GitHub Releases](https://github.com/fastbone/Image-Browse-Opus/releases). Release artifacts are **self-contained** (bundled .NET 10 runtime); you do not need to install the .NET runtime separately for those packages.

| Platform | Artifact | How to run |
|----------|----------|------------|
| **Windows** (x64) | Velopack installer / portable (from release flow) | `ImageBrowse.exe` |
| **macOS** (Apple Silicon) | `ImageBrowse-{version}-macos-arm64.dmg` | Open the DMG, drag **Image Browse**; executable inside the bundle is `ImageBrowse.Avalonia` |
| **macOS** (Intel) | `ImageBrowse-{version}-macos-x64.dmg` | Same as above |
| **Linux** (x64) | `.deb`, `.rpm`, or `ImageBrowse-{version}-linux-x64.tar.gz` | Install with `dpkg` / `rpm`, or extract the archive and run the published executable |

### Distribution notes (Avalonia on Linux and macOS)

- **Linux / LibVLC**: There is **no** `VideoLAN.LibVLC.Linux` package on NuGet. Published **`.deb` / `.rpm`** artifacts declare **`libvlc5`** (Debian/Ubuntu) or **`vlc-libs`** (RPM) so the app can load system VLC. Plain **`.tar.gz`** users should install **`vlc`** or **`libvlc`** from their distro, or video playback and thumbnails may not work.
- **macOS / signing**: Release **DMG** builds are fine for local use. To distribute broadly outside the Mac App Store, maintainers usually **code sign** the app bundle and pass **Apple notarization** so Gatekeeper allows the app on other machines. This repo does not automate signing or notarization.

---

## Features

### Folder Navigation
- **Folder tree** with drives and special folders (Pictures, Desktop, Documents, Downloads) on **Windows**; layout adapts on other platforms
- **Breadcrumb navigation bar** with clickable path segments and direct path editing (`Ctrl+L`)
- **Back / Forward / Up** navigation with full history

### Thumbnail Gallery
- **Virtualized grid** (`ItemsRepeater` + uniform wrap-style layout) for smooth scrolling through large folders
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

### Video Playback (LibVLC)
- **Integrated video player** powered by LibVLC on **Windows (WPF)** and **Avalonia (macOS; Linux with system libvlc)**
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
- **Windows**: WPF-native fast path for common formats (JPEG, PNG, BMP, GIF, TIFF, ICO) with Magick.NET fallback
- **macOS / Linux**: Avalonia native decode for common raster types with Magick.NET fallback

### Appearance
- **Dark theme** and **Light theme** with full UI coverage
- **Transition animations** for dialogs, loading states, and UI elements (can be disabled in settings)
- **Segmented sort controls** in the toolbar for quick sorting
- **Shimmer loading placeholders** for smooth thumbnail loading feedback
- Theme preference persisted across sessions

### Updates
- **Windows**: **Automatic update checking** via Velopack and GitHub Releases; manual check in the About dialog
- **macOS / Linux**: **Velopack**-backed update flow is shared via Core when releases support the platform; otherwise install newer builds from GitHub Releases

---

## Supported Formats

Image Browse supports over **80 image formats** on all platforms (via Magick.NET and native fast paths where available). **Video** playback and **video thumbnails** use LibVLC on Windows (WPF), on Avalonia **macOS** (bundled native libs via NuGet), and on Avalonia **Linux** when VLC/`libvlc` is installed and discoverable by LibVLC.

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

### Fullscreen Viewer (Video, Windows)

When a video is playing in the WPF build, these shortcuts apply instead:

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

### Windows (WPF)

- **OS**: Windows 10 or Windows 11 (64-bit), version 1809 (build 17763) or later
- **Official release builds**: self-contained (no separate .NET install required)
- **Build from source**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and a Windows environment with the **.NET desktop development** workload (or equivalent) for WPF

### macOS (Avalonia)

- **OS**: Recent macOS on **Apple Silicon (arm64)** or **Intel (x64)** matching the DMG you download
- **Official release builds**: self-contained

### Linux (Avalonia)

- **OS**: 64-bit Linux (glibc-based distributions typical for `.deb` / `.rpm`)
- **Official release builds**: self-contained; tarball can be extracted and run from the publish folder

---

## Installation

### From GitHub Releases

1. Open [Releases](https://github.com/fastbone/Image-Browse-Opus/releases).
2. Download the asset for your platform (see [Platforms and downloads](#platforms-and-downloads)).
3. **Windows**: Run the installer or extract the portable layout; start `ImageBrowse.exe`. With Velopack, updates are offered on startup and from **About**.
4. **macOS**: Open the `.dmg`, drag **Image Browse** to Applications (or run from the disk image). The app entry point inside the bundle is `ImageBrowse.Avalonia`.
5. **Linux**: Install the `.deb` or `.rpm` with your package manager, or extract `ImageBrowse-*-linux-x64.tar.gz` and run the executable from the extracted folder.

### Building from Source

#### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- **Windows WPF**: [Visual Studio 2026](https://visualstudio.microsoft.com/) or later with the **.NET desktop development** workload, or another environment that can build **WPF** (`net10.0-windows`)
- **macOS / Linux / cross-compile**: SDK only is enough for the Avalonia project (`net10.0`)

#### Clone

```bash
git clone https://github.com/fastbone/Image-Browse-Opus.git
cd Image-Browse-Opus
```

#### Windows (WPF)

```bash
dotnet restore src/ImageBrowse/ImageBrowse.csproj
dotnet build src/ImageBrowse/ImageBrowse.csproj -c Release
dotnet run --project src/ImageBrowse/ImageBrowse.csproj -c Release
```

Output: `src/ImageBrowse/bin/Release/net10.0-windows/`.

#### macOS / Linux (Avalonia)

```bash
dotnet restore src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj
dotnet build src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release
dotnet run --project src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release
```

For a **self-contained** publish (example):

```bash
dotnet publish src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o publish
# or: -r osx-x64, -r linux-x64
```

Published files are written to the `-o` directory you specify.

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

All cached data (thumbnails, metadata, ratings, tags, settings, sort preferences) is stored in a single SQLite database under the per-user local application data folder:

- **Windows**: `%LocalApplicationData%\ImageBrowse\cache.db`
- **macOS / Linux**: `ImageBrowse/cache.db` under the OS local application data directory (same `SpecialFolder.LocalApplicationData` semantics as .NET)

---

## Architecture

Image Browse follows **MVVM**. Shared logic lives in **ImageBrowse.Core**; each UI stack has its own project.

```
src/ImageBrowse.Core/     Shared models, view models, services, and abstractions
src/ImageBrowse/          Windows WPF host (net10.0-windows)
  Program.cs              Application entry point (Velopack bootstrap)
  App.xaml                Application definition and startup theme
  MainWindow.xaml         Main window shell (toolbar, tree, gallery)
  Views/                  WPF views (gallery, fullscreen, settings, prescan, about)
  Services/               WPF-specific: LibVLC thumbnails, Velopack updates, WPF image loading
  ...
src/ImageBrowse.Avalonia/ macOS & Linux Avalonia host (net10.0)
  Program.cs, App.axaml   Avalonia application entry
  Views/                  Avalonia views (mirrored functionality where supported)
  Services/               Avalonia image loading, thumbnails, dispatcher helpers
  ...
```

---

## Third-Party Libraries

Dependencies are split by **project**. All licenses are compatible with GPL-3.0. Full texts: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Shared (ImageBrowse.Core)

| Library | Version | License | Project |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.1 | MIT | [GitHub](https://github.com/CommunityToolkit/dotnet) |
| Magick.NET-Q8-AnyCPU | 14.11.1 | Apache-2.0 | [GitHub](https://github.com/dlemstra/Magick.NET) |
| MetadataExtractor | 2.9.2 | Apache-2.0 | [GitHub](https://github.com/drewnoakes/metadata-extractor-dotnet) |
| Microsoft.Data.Sqlite | 10.0.5 | Apache-2.0 | [GitHub](https://github.com/dotnet/efcore) |
| Velopack | 0.0.1298 | MIT | [GitHub](https://github.com/velopack/velopack) |

### Windows WPF only (src/ImageBrowse)

| Library | Version | License | Project |
|---|---|---|---|
| LibVLCSharp | 3.9.6 | LGPL-2.1+ | [GitHub](https://github.com/videolan/libvlcsharp) |
| LibVLCSharp.WPF | 3.9.6 | LGPL-2.1+ | [GitHub](https://github.com/videolan/libvlcsharp) |
| VideoLAN.LibVLC.Windows | 3.0.23 | LGPL-2.1+ / GPL-2.0+ | [GitHub](https://github.com/videolan/vlc) |
| VirtualizingWrapPanel | 2.5.1 | MIT | [GitHub](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) |

### macOS / Linux Avalonia only (src/ImageBrowse.Avalonia)

| Library | Version | License | Project |
|---|---|---|---|
| Avalonia | 12.0.0 | MIT | [GitHub](https://github.com/AvaloniaUI/Avalonia) |
| Avalonia.Desktop | 12.0.0 | MIT | [GitHub](https://github.com/AvaloniaUI/Avalonia) |
| Avalonia.Themes.Fluent | 12.0.0 | MIT | [GitHub](https://github.com/AvaloniaUI/Avalonia) |
| Avalonia.Fonts.Inter | 12.0.0 | MIT | [GitHub](https://github.com/AvaloniaUI/Avalonia) |

### Transitive (typical)

| Library | Version | License | Project |
|---|---|---|---|
| XmpCore | 6.1.10.1 | BSD-3-Clause | [GitHub](https://github.com/drewnoakes/xmp-core-dotnet) |
| SQLitePCLRaw | 2.1.11 | Apache-2.0 | [GitHub](https://github.com/ericsink/SQLitePCL.raw) |
| NuGet.Versioning | 6.14.0 | Apache-2.0 | [GitHub](https://github.com/NuGet/NuGet.Client) |

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

A short copyright summary is in [COPYRIGHT](COPYRIGHT). The complete legal text is in [LICENSE](LICENSE).

Copyright (c) 2026 fastbone

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

See [LICENSE](LICENSE) for the full license text.
