# Keyboard shortcuts and keymaps

## For the user

**Settings:** Tools > Keyboard Shortcuts, or the Keyboard page of Options (`Ctrl+P` in the VLC keymap).

| Keymap | What it is |
|---|---|
| VLC (default) | VLC's default hotkeys and menu shortcuts for Windows / Linux, plus keys for Pardeh's own features |
| Pardeh Classic | The keys Pardeh had before (Windows Media Player style) |

- Built-in keymaps are read-only. Changing a key in one makes a copy named "... (custom)" and switches to it.
- Your keymaps: New empty, Duplicate, Rename, Delete, Reset all keys, Import, Export.
- Each action can have several keys. A key belongs to one action; assigning a used key asks before moving it.
- Search by action, category or key name; "Find by key" shows what a pressed key does.
- Shortcuts are off while you type in a text box.

### VLC keymap: the main keys

| Action | Keys |
|---|---|
| Play / pause, stop | Space, S |
| Next, previous | N, P |
| Full screen, leave it | F, Esc |
| Volume, mute | Ctrl+Up / Down, M |
| Seek 3 s / 10 s / 1 min / 5 min | Shift / Alt / Ctrl / Ctrl+Alt + Left or Right |
| Speed: faster, slower, normal, fine | +, -, =, ] and [ |
| Shuffle, repeat | R, L |
| Audio track, subtitle track, subtitles on / off | B, V, Shift+V |
| Subtitle delay | G, H |
| Aspect ratio, crop, picture of the video | A, C, Shift+S |
| Playlist bookmarks: go to, set | F1 to F10, Ctrl+F1 to Ctrl+F10 |
| Open file, files, folder, address, clipboard | Ctrl+O, Ctrl+Shift+O, Ctrl+F, Ctrl+N, Ctrl+V |
| Jump to time, library, equalizer, options, quit | Ctrl+T, Ctrl+L, Ctrl+E, Ctrl+P, Ctrl+Q |
| Minimal view (title bar on / off) | Ctrl+H |

Pardeh's own features: lyrics `Ctrl+Shift+L`, A-B repeat `Shift+L`, markers list `Ctrl+B`, add marker `Ctrl+Shift+B`,
previous / next marker `Ctrl+Shift+Left / Right`, full / skin mode `Ctrl+1 / Ctrl+2`, bars-only window `Ctrl+Shift+M`,
always on top `Ctrl+Shift+A`, desktop widget `Ctrl+Shift+W`, skin size `Ctrl++ / Ctrl+- / Ctrl+0`.

As in VLC, the plain arrow keys belong to disc menus, so they do not seek.

### Not in Pardeh yet

These VLC actions keep their keys but do nothing: next frame, record, show position, audio device, audio delay, subtitle
sync marks, the zoom / autoscale / deinterlace / wallpaper / pixel-crop / 360° / program video actions, disc, title and
chapter navigation, codec information, open disc or capture device, convert, stream, messages log, stream manager.
The editor lists them under "Show actions Pardeh can't do yet". The desktop widget uses `Ctrl+Shift+W`, which is the
stream manager's key in VLC, so that one has none.

Mouse and wheel hotkeys, and VLC's system-wide (global) hotkeys, are not covered. Media keys work as before.

## For developers

| File | Role |
|---|---|
| `Pardeh.Core/Input/KeyChord.cs` | A key with modifiers, as text ("Ctrl+Alt+Left"); no UI toolkit types |
| `Pardeh.Core/Input/KeyCommands.cs` | The command ids and the catalogue (category, name, supported) |
| `Pardeh.Core/Input/BuiltInKeymaps.cs` | The VLC and Pardeh Classic tables |
| `Pardeh.Core/Input/KeymapManager.cs` | Profiles, resolving, editing, saving, import / export |
| `Pardeh.UI/Keymaps.cs` | The app's manager and the Avalonia key translation |
| `Pardeh.UI/PlayerView.Keyboard.cs` | Runs a command |
| `Pardeh.UI/Rendering/OptionsDialog.Keyboard.cs` | The editor page |

**Adding a shortcut:** add a `KeyCmd` id and a catalogue entry, a default chord in each built-in that should have one
(`KeymapTests` fails if a key is used twice), and a `case` in `RunKeyCommand`. In menus pass the id,
`Item("Label", action, KeyCmd.X)`, so the hint follows the keymap. Never write a key name in UI text; use
`Keymaps.WithKey`. Do not change a command id once it has shipped: keymap files store it.

`RunKeyCommand` returns false when a command does not apply (no subtitles to shift, no video), and the key then goes on
to whatever else wants it.

**Storage:** `keymaps.json` in the data folder. A user profile stores only what differs from its built-in base (an empty
list means unbound), so commands added later arrive with their default keys; if such a default is already used by the
user, the user's binding wins. A file that cannot be read is copied to `keymaps.json.bad`.

**Android:** the same keymap applies to attached keyboards and remote keys; Back always leaves full screen. There is no
editor page in the phone settings.
