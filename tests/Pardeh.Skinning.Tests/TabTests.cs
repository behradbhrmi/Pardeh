using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

public sealed class TabTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-tabs-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private const string Sheet = """
        {title: Song}
        [Am]Hello [F]world
        {sot: Intro}
        e|--0--|
        B|--1--|
        {eot}
        [C]more words
        {start_of_tab}
        e|--3--|
        {end_of_tab}
        """;

    [Fact]
    public void Tab_sections_of_a_chord_sheet_are_read_and_stay_out_of_the_words()
    {
        Assert.Equal("Intro\ne|--0--|\nB|--1--|\n\ne|--3--|", TabNotation.FromChordPro(Sheet));
        Assert.DoesNotContain("--0--", ChordNotation.Preprocess(Sheet));
        Assert.Null(TabNotation.FromChordPro("[Am]no tab here"));
    }

    [Fact]
    public void A_tab_file_without_directives_is_read_whole_and_keeps_its_columns()
    {
        Assert.Equal("  Am      F\ne|----    |", TabNotation.FromTabFile("\n\n  Am      F\ne|----\t|   \n\n"));
        Assert.Null(TabNotation.FromTabFile("   \n \n"));
    }

    [Fact]
    public void Wrapping_cuts_every_string_of_a_system_at_the_same_bar_line()
    {
        var tab = "e|--0--0--|--1--1--|--3--3--|\nB|--1--1--|--1--1--|--0--0--|";
        var lines = TabNotation.Wrap(tab, 22);
        Assert.Equal(["e|--0--0--|--1--1--|", "B|--1--1--|--1--1--|", "", "e|--3--3--|", "B|--0--0--|"], lines);
        Assert.All(lines, l => Assert.True(l.Length <= 22));
        Assert.Equal(tab.Split('\n'), TabNotation.Wrap(tab, 80));
    }

    [Fact]
    public void Wrapping_a_system_without_bar_lines_loses_no_notes()
    {
        var tab = "e|" + string.Concat(Enumerable.Range(0, 60).Select(i => (i % 10).ToString()));
        var lines = TabNotation.Wrap(tab, 20).Where(l => l.Length > 0).ToList();
        Assert.All(lines, l => Assert.True(l.Length <= 20 && l.StartsWith("e|")));
        Assert.Equal(tab[2..], string.Concat(lines.Select(l => l[2..])));
    }

    private MediaItem Track(string name) => new(Path.Combine(_folder, name + ".mp3"));

    private static LyricsService Service() => new(null, [new SidecarLyricsSource()])
    {
        ChordSource = new SidecarLyricsSource.Chords(),
        TabSource = (item, cancel) => SidecarLyricsSource.ReadTab(item.Source, cancel),
    };

    [Fact]
    public async Task A_tab_file_next_to_a_song_with_lyrics_comes_with_them()
    {
        File.WriteAllText(Path.Combine(_folder, "a.lrc"), "[00:01.00]one\n[00:02.00]two");
        File.WriteAllText(Path.Combine(_folder, "a.tab"), "e|--0--|");
        var lyrics = await Service().GetAsync(Track("a"), TestContext.Current.CancellationToken);
        Assert.True(lyrics!.Synced);
        Assert.Equal("e|--0--|", lyrics.Tab);
    }

    [Fact]
    public async Task A_song_with_only_a_tab_has_a_tab_and_no_words()
    {
        File.WriteAllText(Path.Combine(_folder, "b.tab"), "e|--0--|");
        var lyrics = await Service().GetAsync(Track("b"), TestContext.Current.CancellationToken);
        Assert.False(lyrics!.HasText);
        Assert.Equal(LyricsSources.TabFile, lyrics.Source);
    }

    [Fact]
    public async Task The_tab_sections_of_a_chord_sheet_come_with_its_chords()
    {
        File.WriteAllText(Path.Combine(_folder, "c.cho"), Sheet);
        var lyrics = await Service().GetAsync(Track("c"), TestContext.Current.CancellationToken);
        Assert.True(lyrics!.HasChords);
        Assert.StartsWith("Intro", lyrics.Tab);
    }
}
