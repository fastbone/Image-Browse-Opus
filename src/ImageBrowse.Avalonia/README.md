# Image Browse Opus (Avalonia)

Cross-platform **macOS** and **Linux** UI for Image Browse Opus. It references [ImageBrowse.Core](../ImageBrowse.Core/) for shared models, view models, and services.

The **Windows** app lives in [ImageBrowse](../ImageBrowse/) (WPF) and is the primary build for LibVLC video playback and Velopack updates.

## Build

From the repository root:

```bash
dotnet restore src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj
dotnet build src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release
dotnet run --project src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release
```

Self-contained publish examples:

```bash
dotnet publish src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -o publish
dotnet publish src/ImageBrowse.Avalonia/ImageBrowse.Avalonia.csproj -c Release -r linux-x64 --self-contained true -o publish
```

See the main [README.md](../../README.md) for platforms, release artifacts, and feature differences between WPF and Avalonia.
