using Avalonia.Input;
using Pardeh.Core.Input;
using Pardeh.UI;
using Xunit;

namespace Pardeh.Skinning.Tests;

public sealed class KeymapTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pardeh-keys-" + Guid.NewGuid().ToString("N"));

    private string Store => Path.Combine(_folder, "keymaps.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static KeyChord Chord(string text) => KeyChord.Parse(text);

    private static KeymapProfile BuiltIn(KeymapManager keys, string id) => keys.Find(id)!;

    [Theory]
    [InlineData("ctrl+shift+left", "Ctrl+Shift+Left")]
    [InlineData("Shift+Alt+o", "Alt+Shift+O")]
    [InlineData("Ctrl++", "Ctrl+Plus")]
    [InlineData("+", "Plus")]
    [InlineData("-", "Minus")]
    [InlineData("=", "Equals")]
    [InlineData("esc", "Escape")]
    [InlineData("[", "[")]
    [InlineData("f11", "F11")]
    public void Chords_have_one_stored_form(string text, string stored) => Assert.Equal(stored, Chord(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+A+B")]
    public void Nonsense_is_not_a_chord(string text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void Built_in_keymaps_name_known_commands_and_give_each_key_to_one()
    {
        var keys = new KeymapManager();
        foreach (var profile in BuiltInKeymaps.All)
        {
            var all = profile.Bindings.SelectMany(pair => pair.Value.Select(key => (Command: pair.Key, Chord: Chord(key)))).ToList();
            Assert.All(all, bound => Assert.NotNull(KeyCommandCatalog.Find(bound.Command)));
            var shared = all.GroupBy(b => b.Chord).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(", ", g.Select(b => b.Command))}");
            Assert.Empty(shared);
            Assert.Equal(all.Count, keys.Resolve(profile).Sum(pair => pair.Value.Count));
        }
    }

    [Fact]
    public void Command_ids_are_unique() =>
        Assert.Equal(KeyCommandCatalog.All.Count, KeyCommandCatalog.All.Select(c => c.Id).Distinct().Count());

    [Theory]
    [InlineData("Space", KeyCmd.PlayPause)]
    [InlineData("S", KeyCmd.Stop)]
    [InlineData("N", KeyCmd.Next)]
    [InlineData("P", KeyCmd.Previous)]
    [InlineData("F", KeyCmd.FullScreen)]
    [InlineData("M", KeyCmd.Mute)]
    [InlineData("Ctrl+Up", KeyCmd.VolumeUp)]
    [InlineData("Shift+Left", KeyCmd.BackExtraShort)]
    [InlineData("Alt+Right", KeyCmd.ForwardShort)]
    [InlineData("Ctrl+Left", KeyCmd.BackMedium)]
    [InlineData("Ctrl+Alt+Right", KeyCmd.ForwardLong)]
    [InlineData("F1", "bookmark.play1")]
    [InlineData("Ctrl+F10", "bookmark.set10")]
    [InlineData("Ctrl+Q", KeyCmd.Quit)]
    public void Vlc_is_the_default(string key, string command)
    {
        var keys = new KeymapManager();
        Assert.Equal(BuiltInKeymaps.VlcId, keys.Active.Id);
        Assert.Equal(command, keys.CommandFor(Chord(key)));
    }

    [Fact]
    public void Switching_profiles_switches_keys()
    {
        var keys = new KeymapManager();
        Assert.Equal(KeyCmd.PlayBookmark(7), keys.CommandFor(Chord("F7")));
        keys.SetActive(BuiltIn(keys, BuiltInKeymaps.ClassicId));
        Assert.Equal(KeyCmd.Mute, keys.CommandFor(Chord("F7")));
        Assert.Equal(KeyCmd.PlayPause, keys.CommandFor(Chord("Ctrl+P")));
    }

    [Fact]
    public void A_built_in_cannot_be_changed_but_makes_a_copy()
    {
        var keys = new KeymapManager();
        var vlc = keys.Active;
        Assert.Throws<InvalidOperationException>(() => keys.AddChord(vlc, KeyCmd.Stop, Chord("Q")));
        Assert.Throws<InvalidOperationException>(() => keys.Delete(vlc));

        var copy = keys.Editable(vlc);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(copy, keys.Active);
        Assert.Equal(BuiltInKeymaps.VlcId, copy.BaseId);
        Assert.Equal(KeyCmd.Stop, keys.CommandFor(Chord("S")));
        Assert.Same(copy, keys.Editable(copy));
    }

    [Fact]
    public void Giving_a_key_away_takes_it_from_its_old_command_for_good()
    {
        var keys = new KeymapManager();
        var mine = keys.Editable(keys.Active);

        Assert.Equal(KeyCmd.Next, keys.AddChord(mine, KeyCmd.Stop, Chord("N")));
        Assert.Equal(KeyCmd.Stop, keys.CommandFor(Chord("N")));
        Assert.Equal(["MediaNextTrack"], keys.ChordsFor(KeyCmd.Next).Select(c => c.ToString()));

        keys.RemoveChord(mine, KeyCmd.Stop, Chord("N"));
        Assert.Null(keys.CommandFor(Chord("N"))); // it does not drift back to "next"
        Assert.False(keys.IsModified(mine, KeyCmd.Stop)); // stop has its own keys again
        Assert.True(keys.IsModified(mine, KeyCmd.Next));
    }

    [Fact]
    public void Resetting_one_command_takes_its_default_keys_back()
    {
        var keys = new KeymapManager();
        var mine = keys.Editable(keys.Active);
        keys.AddChord(mine, KeyCmd.Stop, Chord("N"));

        keys.ResetCommand(mine, KeyCmd.Next);

        Assert.Equal(KeyCmd.Next, keys.CommandFor(Chord("N")));
        Assert.Equal(["S", "MediaStop"], keys.ChordsFor(KeyCmd.Stop).Select(c => c.ToString()));
        Assert.False(keys.HasChanges(mine));
    }

    [Fact]
    public void Reset_all_returns_to_the_base()
    {
        var keys = new KeymapManager();
        var mine = keys.Editable(keys.Active);
        keys.AddChord(mine, KeyCmd.Stop, Chord("Q"));
        keys.RemoveChord(mine, KeyCmd.PlayPause, Chord("Space"));

        keys.ResetAll(mine);

        Assert.Equal(KeyCmd.PlayPause, keys.CommandFor(Chord("Space")));
        Assert.Null(keys.CommandFor(Chord("Q")));
    }

    [Fact]
    public void An_empty_keymap_has_no_keys()
    {
        var keys = new KeymapManager();
        var empty = keys.Create("Blank");
        keys.SetActive(empty);
        Assert.Null(keys.CommandFor(Chord("Space")));
        keys.AddChord(empty, KeyCmd.PlayPause, Chord("Space"));
        Assert.Equal(KeyCmd.PlayPause, keys.CommandFor(Chord("Space")));
    }

    [Fact]
    public void Names_stay_unique_and_deleting_the_one_in_use_goes_to_its_base()
    {
        var keys = new KeymapManager();
        var classic = BuiltIn(keys, BuiltInKeymaps.ClassicId);
        var first = keys.Duplicate(classic, "Mine");
        var second = keys.Duplicate(first, "mine");
        Assert.Equal("mine 2", second.Name);
        keys.Rename(second, "VLC");
        Assert.Equal("VLC 2", second.Name);

        keys.SetActive(second);
        keys.Delete(second);
        Assert.Equal(BuiltInKeymaps.ClassicId, keys.Active.Id);
    }

    [Fact]
    public void Profiles_and_the_one_in_use_survive_a_restart()
    {
        var keys = new KeymapManager(Store);
        var mine = keys.Editable(keys.Active);
        keys.AddChord(mine, KeyCmd.Stop, Chord("Ctrl+Plus"));
        keys.RemoveChord(mine, KeyCmd.PlayPause, Chord("Space"));

        var again = new KeymapManager(Store);

        Assert.Null(again.Problem);
        Assert.Equal(mine.Id, again.Active.Id);
        Assert.Equal(KeyCmd.Stop, again.CommandFor(Chord("Ctrl+Plus")));
        Assert.Null(again.CommandFor(Chord("Space")));
        Assert.Contains("\"Ctrl+Plus\"", File.ReadAllText(Store));
    }

    [Fact]
    public void A_users_key_wins_over_a_default_the_base_gives_another_command()
    {
        // As after an update that gave a command a key the user had already used for something else.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Store, """
            { "version": 1, "active": "user-a", "profiles": [
              { "id": "user-a", "name": "Old", "base": "vlc", "bindings": { "playback.record": [ "Space" ], "from.the.future": [ "Q" ] } } ] }
            """);

        var keys = new KeymapManager(Store);

        Assert.Equal(KeyCmd.Record, keys.CommandFor(Chord("Space")));
        Assert.Equal(["MediaPlayPause"], keys.ChordsFor(KeyCmd.PlayPause).Select(c => c.ToString()));
        keys.Rename(keys.Active, "Newer");
        Assert.Contains("from.the.future", File.ReadAllText(Store)); // kept for the version that knows it
    }

    [Fact]
    public void A_damaged_file_is_kept_aside_and_vlc_is_used()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Store, "{ not json");

        var keys = new KeymapManager(Store);

        Assert.NotNull(keys.Problem);
        Assert.Equal(BuiltInKeymaps.VlcId, keys.Active.Id);
        Assert.Equal("{ not json", File.ReadAllText(Store + ".bad"));
    }

    [Fact]
    public void Export_and_import_carry_a_profile_across()
    {
        var keys = new KeymapManager();
        var mine = keys.Editable(keys.Active);
        keys.AddChord(mine, KeyCmd.Stop, Chord("Q"));

        var other = new KeymapManager();
        var imported = other.Import(keys.Export(mine));
        other.SetActive(imported);

        Assert.Equal(KeyCmd.Stop, other.CommandFor(Chord("Q")));
        Assert.Equal(KeyCmd.PlayPause, other.CommandFor(Chord("Space")));

        var builtIn = other.Import(other.Export(BuiltIn(other, BuiltInKeymaps.ClassicId)));
        other.SetActive(builtIn);
        Assert.Equal(KeyCmd.Mute, other.CommandFor(Chord("F7")));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("{}")]
    [InlineData("""{ "pardehKeymap": 99, "name": "x", "bindings": {} }""")]
    [InlineData("""{ "pardehKeymap": 1, "name": "x", "bindings": { "playback.stop": [ "Hyper+S" ] } }""")]
    public void Importing_something_else_is_refused(string text)
    {
        var keys = new KeymapManager();
        Assert.Throws<FormatException>(() => keys.Import(text));
        Assert.Equal(2, keys.Profiles.Count);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("1:02:03", 3723)]
    public void Jump_to_time_reads_clock_text(string text, double seconds)
    {
        Assert.True(JumpTime.TryParse(text, out var parsed));
        Assert.Equal(seconds, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    public void Jump_to_time_refuses_other_text(string text) => Assert.False(JumpTime.TryParse(text, out _));

    [Theory]
    [InlineData(Key.S, KeyModifiers.None, null, "S")]
    [InlineData(Key.D1, KeyModifiers.Control, null, "Ctrl+1")]
    [InlineData(Key.OemPlus, KeyModifiers.Shift, null, "Plus")]
    [InlineData(Key.OemPlus, KeyModifiers.None, null, "Equals")]
    [InlineData(Key.OemPlus, KeyModifiers.None, "+", "Plus")]       // a layout with + unshifted
    [InlineData(Key.OemPlus, KeyModifiers.Control, null, "Ctrl+Equals")]
    [InlineData(Key.Add, KeyModifiers.None, null, "Plus")]
    [InlineData(Key.Subtract, KeyModifiers.None, null, "Minus")]
    [InlineData(Key.OemOpenBrackets, KeyModifiers.None, null, "[")]
    [InlineData(Key.PageDown, KeyModifiers.None, null, "PageDown")]
    [InlineData(Key.Enter, KeyModifiers.Alt, null, "Alt+Enter")]
    [InlineData(Key.F10, KeyModifiers.Control, null, "Ctrl+F10")]
    [InlineData(Key.NumPad0, KeyModifiers.Control, null, "Ctrl+NumPad0")]
    [InlineData(Key.MediaPlayPause, KeyModifiers.None, null, "MediaPlayPause")]
    public void Avalonia_keys_become_chords(Key key, KeyModifiers modifiers, string? symbol, string expected) =>
        Assert.Equal(expected, Keymaps.ChordOf(key, modifiers, symbol)!.Value.ToString());

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.None)]
    public void A_modifier_alone_is_no_chord(Key key) => Assert.Null(Keymaps.ChordOf(key, KeyModifiers.None));

    [Fact]
    public void Every_built_in_key_can_be_pressed_and_shown_in_a_menu()
    {
        foreach (var chord in BuiltInKeymaps.All.SelectMany(p => p.Bindings.Values).SelectMany(k => k).Select(Chord))
        {
            var gesture = Keymaps.GestureOf(chord);
            Assert.True(gesture is not null, chord.ToString());
            // Pressing the gesture gives the chord back ("Plus" is typed with Shift or on the number pad).
            var held = chord.Key == "Plus" ? gesture!.KeyModifiers | KeyModifiers.Shift : gesture!.KeyModifiers;
            Assert.Equal(chord, Keymaps.ChordOf(gesture.Key, held));
        }
    }
}
