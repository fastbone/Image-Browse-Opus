# Unix file-type registration (Linux / macOS)

Windows uses the registry for “Open with” associations via the in-app **FileAssociationService** in the WPF build. There is no single cross-Unix equivalent, so maintainers typically ship one of the following.

## Linux (`.desktop` entry)

1. Create a `.desktop` file in `~/.local/share/applications/` (per user) or `/usr/share/applications/` (system), for example `image-browse.desktop`:

```ini
[Desktop Entry]
Type=Application
Name=Image Browse
Exec=/opt/image-browse/ImageBrowse.Avalonia %F
Icon=image-browse
MimeType=image/jpeg;image/png;image/webp;video/mp4;
Categories=Graphics;Viewer;
```

2. Adjust `Exec` to the installed binary path and extend `MimeType` as needed.
3. Run `update-desktop-database ~/.local/share/applications` if your distro requires it.

## macOS (bundle `Info.plist`)

For a signed **app bundle**, declare supported document types in `Info.plist` under `CFBundleDocumentTypes` / `UTExportedTypeDeclarations`, mapping extensions and UTIs to your app. Notarization and code signing are required for a smooth “Open with” experience outside developer mode.

## Notes

- This repository does not run these steps automatically; treat them as packaging or post-install documentation.
- Flatpak, Snap, and distro packages each have their own metadata formats (e.g. Flatpak `mime` keys); align with your chosen distribution channel.
