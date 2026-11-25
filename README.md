<img src="docs/img/pardeh-logo-512.png" width="96" alt="Pardeh logo" align="right">

# Pardeh

A modern media player for Windows 10/11 that brings back the classic look of vintage players, starting with the 2003 "9 Series" era. Classic Windows Media Player skins (`.wmz`) load natively, scripts and all, on a modern .NET engine.

> Pardeh (پرده) means *screen* and, in Persian music, a *tone*: what you watch and what you hear.

## Status

Milestone 4: **full mode, skin mode, audio and video**.

- Loads WMP 7–11 `.wmz` / `.wms` skins: layout, bitmaps, colour-key transparency, button groups with mapping images, sliders, text, playlist, equalizer, slide-out panels, view switching.
- Loads **Winamp 2.x classic `.wsz`** skins (main window: transport, clock, scrolling title, volume / balance / seek, shuffle / repeat, spectrum bars; no equalizer or playlist window, no `region.txt` shapes). See [docs/04](docs/04-theme-plugin-architecture.md).
- Runs skin JScript in a sandboxed engine (Jint) against a WMP-compatible object model (`player`, `theme`, `view`, `eq` …).
- Audio playback with **Nava**: FFmpeg decoding (Windows Media Foundation as fallback), 10-band EQ, volume/balance/mute, spectrum for visualizations, WASAPI output.
- Video playback (FFmpeg or Media Foundation, synced to the audio clock; video without sound uses a timer clock). Skins that resize to fit the video are capped at 85% of the screen.
- Shaped window: transparent pixels are click-through, like the original skinned players. Whole-number pixel scaling on 125–200% displays.
- Keyboard shortcuts with keymap profiles: VLC's default keys out of the box, "Pardeh Classic" (the Windows Media Player style keys) as a second built-in, and your own profiles with an editor (Tools > Keyboard Shortcuts), import and export. Media keys work too. See [docs/17-keyboard-shortcuts.md](docs/17-keyboard-shortcuts.md). Key names elsewhere in this README may be the Pardeh Classic ones.
- Right-click menu with a Skins submenu; dropped or added skins are copied to `%APPDATA%\Pardeh\Skins`, and the last skin is reopened on start.
- **Full mode** (default): the classic main window with the taskbar rail, menu bar (File, View, Play, Tools, Help), Now Playing (visualization or video + playlist), and the **Media Library** (Artist / Album / Genre / Year tree, track list, double-click to play). Its art is Pardeh's own drawing of the 2003 look; images in `%APPDATA%\Pardeh\FullMode` (or `private/fullmode`) replace generated ones by file name. Switch modes with Ctrl+1 / Ctrl+2, View menu, or the skin's return button.
- Media Library scans your Music and Videos folders (File › Add Folder to Library for more) and caches tags in `%APPDATA%\Pardeh\library.json`.
- Not yet: Copy from CD, Copy to CD or Device, Radio Tuner, a visual Skin Chooser, Options dialog.
- **Android** (phone skins, media session, device music): see [docs/android-build.md](docs/android-build.md).

See also the [WMP skin format](docs/03-wmz-skin-format.md) and the [theme and skin architecture](docs/04-theme-plugin-architecture.md).

## Build and run

Built on .NET 11 and Avalonia 12.1.5 (Skia). Requires the .NET 11 SDK. For every audio/video format, fetch FFmpeg once (LGPL build, about 80 MB, not committed):

```powershell
.\scripts\get-ffmpeg.ps1
```

Without it, Pardeh falls back to Windows Media Foundation (MP3, WMA, WAV, AAC, FLAC, MP4, WMV …).

```powershell
dotnet build Pardeh.slnx
dotnet run --project src/Pardeh.App -- path\to\Skin.wmz [music files...]
```

With no skin argument, Pardeh uses the first `.wmz` in `%APPDATA%\Pardeh\Skins`, or (for development) `private/skins/` in the repo.

- Drag media files onto the player to play them; drag a `.wmz` or Winamp `.wsz` onto it to switch skins.
- Online video and radio: open or drop the direct address of a media file or stream (HLS included). A web page's address plays only when the page publishes its video's address; many sites, YouTube among them, do not.
- Right-click the skin for Open File, Choose Skin and Exit.

## Package and install

```powershell
.\scripts\get-ffmpeg.ps1          # once
.\scripts\publish.ps1 -Version 0.1.0
```

This makes `dist\Pardeh-0.1.0-win-x64.zip` (needs the .NET 11 desktop runtime). Unzip it and run `install.ps1` (no admin rights): Pardeh goes to `%LOCALAPPDATA%\Programs\Pardeh` with a Start menu shortcut and an uninstall entry in Settings › Apps. Tools › Options › File Types registers it for media files.

## Tests

```powershell
dotnet run --project tests/Pardeh.Skinning.Tests
```

Tests that need a real WMP skin look for `private/skins/9SeriesDefault.wmz` and are skipped when it's missing.

## Third-party skins

Pardeh ships no Microsoft, Winamp or other third-party skins (the Winamp tests draw a tiny synthetic `.wsz` in code). The `private/` folder is git-ignored. Keep reference skins and captures there.

## Third-party libraries

- **.NET packages:** Avalonia, FFmpeg.AutoGen and NAudio (MIT), Jint (BSD-2-Clause), TagLibSharp (LGPL-2.1). The optional
  Windows 11 widget also uses the Windows App SDK, under Microsoft's license.
- **Icons:** some come from Microsoft's Fluent UI System Icons (MIT).
- **FFmpeg** is used as dynamically loaded shared libraries under the LGPL; Pardeh does not modify it. Windows: [BtbN builds](https://github.com/BtbN/FFmpeg-Builds) (`scripts/get-ffmpeg.ps1`). Android: LGPL-3.0 builds from [tqk2811/FFmpegBuild](https://github.com/tqk2811/FFmpegBuild) (`scripts/get-ffmpeg-android.ps1`); source is at [ffmpeg.org](https://ffmpeg.org/download.html) (tag n8.1.2) and the license text ships next to the libraries. You can replace the `.so` files with your own build.

## Design credits

- **Ember** (a built-in full mode) adapts the design of the [VLC Redesign by Juxtopposed](https://www.juxtopposed.com/), licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Pardeh's version is redrawn; it uses no VLC code, artwork or trademarks.
- **Horizon** follows the layout of the community [Windows Media Player 9 web app clone](https://github.com/rmellis/Windows-Media-Player-9-WebApp-Clone); its art is Pardeh's own.
- The Pardeh logo is AI-generated art. A personal logo can be used locally by placing `logo.png` in `%APPDATA%\Pardeh` (never committed or packaged).

## Projects

| Project | Purpose |
| --- | --- |
| `Pardeh.Core` | Player model: playlist, transport state machine, equalizer settings |
| `Pardeh.Nava` | Media engine: FFmpeg decoding (Media Foundation fallback), 10-band EQ, spectrum, WASAPI output, video frames timed to the audio clock |
| `Pardeh.Skinning` | Skin loader, element runtime, script host, WMP object model (no UI dependency) |
| `Pardeh.App` | Avalonia desktop app: skin window, pixel renderer, dialogs |

## Diagnostics

- `PARDEH_SNAPSHOT=out.png` (optional `PARDEH_SNAPSHOT_DELAY=seconds`): renders the skin to a PNG after start, without needing window focus.
- `PARDEH_SNAPSHOT_MENU=View` (or `View/7` to also open item 7's submenu), with `PARDEH_SNAPSHOT`: opens that menu and renders it to `<snapshot>-menu.png` (and `-submenu.png`, `-menu-selected.png`). Popup bitmaps don't show hover highlights or submenu gutters reliably; check those in the log or by hand.
- `PARDEH_FULLMODE=horizon|pardeh|wmp9`: forces a full mode look for this run (normally chosen in Tools › Options › Player or View › Full Mode Look).
- `PARDEH_SNAPSHOT_DIALOG=options/2` (Options on tab 2) or `prompt`, with `PARDEH_SNAPSHOT`: opens that dialog and renders it to `<snapshot>-dialog.png`.
- `PARDEH_DIAG=1`: logs player and visualization state once per second.
- `PARDEH_LIBRARY_FOLDERS=a;b`: scan these folders instead of Music and Videos.
- `PARDEH_DATA_DIR=folder`: use this folder instead of `%APPDATA%\Pardeh`, so test runs and snapshots leave your settings and library alone.
- `PARDEH_MUTE=1`: start muted (silent snapshot runs).
- `PARDEH_NO_FFMPEG=1`: force the Media Foundation decoders.
- Log file: `%APPDATA%\Pardeh\pardeh.log`.

## License

Pardeh is free software under the [GNU General Public License, version 3](LICENSE). If you distribute it, or an app built on
its code, you must provide the source under the same license. It comes with no warranty. Third-party libraries and designs
keep their own licenses (above).

Pardeh is an independent project. It is not affiliated with or endorsed by Microsoft, Winamp or any other company whose
products it takes inspiration from. Their names and trademarks belong to their owners.
