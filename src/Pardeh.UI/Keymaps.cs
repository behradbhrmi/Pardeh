using Avalonia.Input;
using Pardeh.Core.Input;

namespace Pardeh.UI;

/// <summary>The app's keymaps, and the translation between Avalonia's keys and the ones keymaps store.</summary>
internal static class Keymaps
{
    private static KeymapManager? _current;

    public static KeymapManager Current => _current ??= new KeymapManager(Path.Combine(AppPaths.DataDirectory, "keymaps.json"));

    /// <summary>Keys whose keymap name is not Avalonia's own. Several Avalonia keys have two names, so none rely on ToString.</summary>
    private static readonly (Key Key, string Name)[] Named =
    [
        (Key.Space, "Space"), (Key.Enter, "Enter"), (Key.Escape, "Escape"), (Key.Tab, "Tab"), (Key.Back, "Back"),
        (Key.Delete, "Delete"), (Key.Insert, "Insert"), (Key.Home, "Home"), (Key.End, "End"),
        (Key.PageUp, "PageUp"), (Key.PageDown, "PageDown"),
        (Key.Left, "Left"), (Key.Right, "Right"), (Key.Up, "Up"), (Key.Down, "Down"),
        (Key.PrintScreen, "PrintScreen"), (Key.Pause, "Pause"),
        (Key.MediaPlayPause, "MediaPlayPause"), (Key.MediaStop, "MediaStop"),
        (Key.MediaNextTrack, "MediaNextTrack"), (Key.MediaPreviousTrack, "MediaPreviousTrack"),
        (Key.VolumeUp, "VolumeUp"), (Key.VolumeDown, "VolumeDown"), (Key.VolumeMute, "VolumeMute"),
        (Key.OemOpenBrackets, "["), (Key.OemCloseBrackets, "]"), (Key.OemSemicolon, ";"), (Key.OemQuotes, "'"),
        (Key.OemComma, ","), (Key.OemPeriod, "."), (Key.OemQuestion, "/"), (Key.OemPipe, "\\"), (Key.OemTilde, "`"),
    ];

    /// <summary>
    /// The chord of a key press, or null for a modifier on its own (or a key with no name).
    /// <paramref name="symbol"/> is the character the key typed, where the system reports it: layouts differ in which
    /// key writes + and =.
    /// </summary>
    public static KeyChord? ChordOf(Key key, KeyModifiers modifiers, string? symbol = null)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.System or Key.CapsLock or Key.NumLock or Key.Scroll)
            return null;

        var held = ChordModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
            held |= ChordModifiers.Ctrl;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            held |= ChordModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            held |= ChordModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            held |= ChordModifiers.Win;

        // The main keyboard's + / = key: the character decides, and Shift is only how it was typed.
        if (key == Key.OemPlus)
        {
            var plus = symbol == "+" || symbol != "=" && held.HasFlag(ChordModifiers.Shift);
            return new KeyChord(plus ? "Plus" : "Equals", held & ~ChordModifiers.Shift);
        }

        string name;
        if (key is >= Key.A and <= Key.Z)
            name = ((char)('A' + (key - Key.A))).ToString();
        else if (key is >= Key.D0 and <= Key.D9)
            name = ((char)('0' + (key - Key.D0))).ToString();
        else if (key == Key.Add)
            name = "Plus";
        else if (key is Key.OemMinus or Key.Subtract)
            name = "Minus";
        else
            name = Array.Find(Named, n => n.Key == key).Name ?? key.ToString();

        return new KeyChord(name, held);
    }

    public static KeyChord? ChordOf(KeyEventArgs e) => ChordOf(e.Key, e.KeyModifiers, e.KeySymbol);

    /// <summary>The chord as Avalonia's gesture, for the key hint beside a menu item. Display only.</summary>
    public static KeyGesture? GestureOf(KeyChord chord)
    {
        var key = chord.Key switch
        {
            "Plus" => Key.OemPlus,
            "Equals" => Key.OemPlus,
            "Minus" => Key.OemMinus,
            [>= 'A' and <= 'Z'] => Key.A + (chord.Key[0] - 'A'),
            [>= '0' and <= '9'] => Key.D0 + (chord.Key[0] - '0'),
            _ => Array.Find(Named, n => n.Name == chord.Key) is { Name: not null } named ? named.Key
                : Enum.TryParse<Key>(chord.Key, ignoreCase: true, out var parsed) ? parsed : Key.None,
        };
        if (key == Key.None)
            return null;
        var modifiers = KeyModifiers.None;
        if (chord.Modifiers.HasFlag(ChordModifiers.Ctrl))
            modifiers |= KeyModifiers.Control;
        if (chord.Modifiers.HasFlag(ChordModifiers.Alt))
            modifiers |= KeyModifiers.Alt;
        if (chord.Modifiers.HasFlag(ChordModifiers.Shift))
            modifiers |= KeyModifiers.Shift;
        if (chord.Modifiers.HasFlag(ChordModifiers.Win))
            modifiers |= KeyModifiers.Meta;
        return new KeyGesture(key, modifiers);
    }

    /// <summary>The first key of a command in the keymap in use, as a menu hint; null when it has none.</summary>
    public static KeyGesture? Hint(string? command) =>
        command is not null && Current.ChordsFor(command) is { Count: > 0 } chords ? GestureOf(chords[0]) : null;

    /// <summary>The first key of a command as text ("Ctrl+L"), or null.</summary>
    public static string? KeyText(string command) =>
        Current.ChordsFor(command) is { Count: > 0 } chords ? chords[0].ToDisplayString() : null;

    /// <summary>"Close the lyrics (Ctrl+L)": the text, with the command's key when it has one.</summary>
    public static string WithKey(string text, string command) => KeyText(command) is { } key ? $"{text} ({key})" : text;
}
