# WMP skin format (.wmz) — analysis of "9 Series Default"

Source: `private/skins/9SeriesDefault.wmz` (git-ignored; Microsoft copyright, never commit). Analysed 2026-11-04.

## What it is

The file is the **"Corona"** theme, titled "9 Series Default": WMP9's default **skin mode** (the compact player). It is **not** full mode. Full mode (taskbar rail, Media Library, Copy from CD, and so on) is built into `wmploc.dll` and isn't shipped as a .wmz.

A `.wmz` is a plain **ZIP** containing:

| Part | Files | Notes |
| --- | --- | --- |
| Layout | `Corona.wms` (UTF-16 XML, 1,074 lines) | Declarative tree of views, subviews and controls with exact pixel coordinates |
| Script | `Corona.js`, `corona_tiny.js`, `metadata.js` (UTF-16 JScript) | Event handlers and behaviour |
| Bitmaps | 116 × `.bmp` | Magenta `#FF00FF` = transparent; each control has `normal`, `_hover`, `_down`, `_disabled` variants |
| Hit maps | `*_map.bmp` | Colour-coded regions: each button in a `BUTTONGROUP` owns one `mappingColor` (e.g. `#815490` = Open File) |

The scripts also load `res://wmploc.dll/RT_TEXT/#132`, a shared script inside `wmploc.dll`. We need that file (from an XP VM) or must re-implement its functions.

## Views

| View | Size (min) | Purpose |
| --- | --- | --- |
| `vPlayer` | 859 × 468 | Main skin: video/viz, top controls, transport, seek, slide-out playlist drawer and EQ panel |
| `viewTiny` | 596 × 468 | Reduced ("minimize") variant |

The view sizes include room for the slide-out drawers; the visible player core is ~346 × 289.

## Elements used (count)

`SUBVIEW` 33 · `TEXT` 15 · `BUTTON` 14 · `SLIDER` 10 · `BUTTONELEMENT` 10 · `BUTTONGROUP` 6 · `VOLUMESLIDER` 2 · `SEEKSLIDER` 2 · `PLAYELEMENT`/`PAUSEBUTTON`/`STOPELEMENT`/`PREVELEMENT`/`NEXTELEMENT` 2 each · `RETURNBUTTON` 2 · `REWBUTTON`/`FFWDBUTTON`/`SHUFFLEBUTTON` · `PLAYLIST` · `DROPDOWNPLAYLIST` · `EQUALIZERSETTINGS` · `BALANCESLIDER` · `WMPEFFECTS` (visualizations) · `WMPVIDEO` · `POPUP` · `PLAYER` / `NETWORK` (event hooks)

Key attributes: `left/top/width/height` (numbers or `JScript:` expressions), `horizontalAlignment`/`verticalAlignment` (`left|right|stretch|…`, i.e. anchoring on resize), `backgroundImage`, `backgroundTiled`, `transparencyColor`, `image/hoverImage/downImage/disabledImage/mappingImage`, `sticky`, `enabled="wmpprop:…"` (data binding), `upToolTip/downToolTip`, `onClick`, `zIndex`.

## Object model the scripts call (most used)

`eq.gainLevel`, `player.status`, `player.openState`, `player.playState`, `player.controls.play/pause/fastForward/fastReverse`, `player.settings.mute`, `player.currentMedia.name/getItemInfo/imageSourceWidth/Height`, `player.currentPlaylist.getItemInfo`, `player.network.bufferingProgress/sourceProtocol`, `player.dvd.isAvailable`, `player.launchURL`, `theme.loadString/loadPreference/savePreference/currentViewID/openDialog`, `view.width/height/minWidth/minHeight/minimize/close/timerInterval`, `vis.visible/next`, `eq.bypass`.

The surface is small and well documented (the WMP 9 SDK skin reference), so it's realistic to emulate.

## Implications for Pardeh

1. **Pardeh's skin engine loads `.wmz` natively.** It needs: a ZIP reader, a WMS XML parser, BMP decoding with colour-key transparency, mapping-image hit testing, `JScript:` layout expressions, `wmpprop:` bindings, and a JavaScript engine ([Jint](https://github.com/sebastienros/jint), pure .NET) running the scripts against an emulated WMP object model backed by `Pardeh.Core`.
2. **Skin mode is pixel-exact by construction**: we render the original bitmaps at the original coordinates.
3. **Vintage themes for free:** thousands of classic WMP 7–11 skins use this format. Users supply their own `.wmz` files; the repo ships none.
4. **Full mode still has to be authored**, ideally in the same WMS-style format (as a `Pardeh` skin). For exact pixels, copy `C:\WINDOWS\system32\wmploc.dll` from the XP VM into `private/`. Its bitmap resources hold the full-mode chrome, and it also contains `RT_TEXT #132`.
5. **Open-source build:** ships an original Pardeh default skin plus the engine. Microsoft assets stay in `private/` (git-ignored) for personal use and pixel-reference tests.
