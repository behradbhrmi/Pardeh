namespace Pardeh.Core.Input;

[Flags]
public enum ChordModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// One key press with its modifiers, as keymaps store it: "Ctrl+Alt+Left", "Shift+S", "Plus".
/// <see cref="Key"/> is a UI-toolkit-neutral name: "A" … "Z", "0" … "9", "F1" …, "NumPad0" …, "Plus", "Minus", "Equals",
/// "[", "]", "Space", "Enter", "Escape", "PageUp", "MediaPlayPause" and the like. "Plus" is the character +, however the
/// keyboard produces it, so it never carries Shift for that reason alone.
/// </summary>
public readonly record struct KeyChord
{
    public KeyChord(string key, ChordModifiers modifiers = ChordModifiers.None)
    {
        Key = NormalizeKey(key);
        Modifiers = modifiers;
    }

    public string Key { get; }

    public ChordModifiers Modifiers { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Key);

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "Escape", ["Return"] = "Enter", ["Del"] = "Delete", ["Ins"] = "Insert",
        ["PgUp"] = "PageUp", ["PgDn"] = "PageDown", ["Prior"] = "PageUp", ["Next"] = "PageDown",
        ["+"] = "Plus", ["-"] = "Minus", ["="] = "Equals",
    };

    /// <summary>The names with more than one letter, in the casing keymaps use.</summary>
    private static readonly string[] Named =
    [
        "Plus", "Minus", "Equals", "Space", "Enter", "Escape", "Tab", "Back", "Delete", "Insert", "Home", "End",
        "PageUp", "PageDown", "Left", "Right", "Up", "Down", "PrintScreen", "Pause",
        "MediaPlayPause", "MediaStop", "MediaNextTrack", "MediaPreviousTrack", "VolumeUp", "VolumeDown", "VolumeMute",
    ];

    private static string NormalizeKey(string key)
    {
        key = (key ?? string.Empty).Trim();
        if (Aliases.TryGetValue(key, out var alias))
            return alias;
        if (key.Length == 1)
            return key.ToUpperInvariant();
        return Array.Find(Named, n => n.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? (key.Length > 0 ? char.ToUpperInvariant(key[0]) + key[1..] : key);
    }

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var rest = text.Trim();
        var modifiers = ChordModifiers.None;
        // The key may itself be "+" ("Ctrl++"), so modifiers are taken off the front one at a time.
        while (true)
        {
            var at = rest.IndexOf('+');
            if (at <= 0 || at == rest.Length - 1)
                break;
            ChordModifiers? modifier = rest[..at].Trim().ToLowerInvariant() switch
            {
                "ctrl" or "control" => ChordModifiers.Ctrl,
                "alt" => ChordModifiers.Alt,
                "shift" => ChordModifiers.Shift,
                "win" or "meta" => ChordModifiers.Win,
                _ => null,
            };
            if (modifier is null)
                return false;
            modifiers |= modifier.Value;
            rest = rest[(at + 1)..].Trim();
        }

        if (rest.Length == 0 || rest.Length > 1 && rest.Contains('+'))
            return false;
        chord = new KeyChord(rest, modifiers);
        return true;
    }

    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"Not a key: '{text}'");

    /// <summary>The stored form, "Ctrl+Shift+Plus".</summary>
    public override string ToString() => Prefix() + Key;

    /// <summary>The form shown to people, "Ctrl+Shift++".</summary>
    public string ToDisplayString() => Prefix() + Key switch
    {
        "Plus" => "+",
        "Minus" => "-",
        "Equals" => "=",
        "Escape" => "Esc",
        "PageUp" => "Page Up",
        "PageDown" => "Page Down",
        "Back" => "Backspace",
        "MediaPlayPause" => "Media Play/Pause",
        "MediaStop" => "Media Stop",
        "MediaNextTrack" => "Media Next",
        "MediaPreviousTrack" => "Media Previous",
        "VolumeUp" => "Volume Up key",
        "VolumeDown" => "Volume Down key",
        "VolumeMute" => "Mute key",
        _ => Key ?? string.Empty,
    };

    private string Prefix() =>
        (Modifiers.HasFlag(ChordModifiers.Ctrl) ? "Ctrl+" : "") +
        (Modifiers.HasFlag(ChordModifiers.Alt) ? "Alt+" : "") +
        (Modifiers.HasFlag(ChordModifiers.Shift) ? "Shift+" : "") +
        (Modifiers.HasFlag(ChordModifiers.Win) ? "Win+" : "");
}
