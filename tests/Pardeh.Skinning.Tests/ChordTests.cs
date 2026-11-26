using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

[Collection("MediaSources statics")]
public sealed class ChordTests : IDisposable
{
    private readonly string _data = NewTempFolder("pardeh-chord-data-");
    private readonly string _music = NewTempFolder("pardeh-chord-music-");

    public void Dispose()
    {
        Directory.Delete(_data, recursive: true);
        Directory.Delete(_music, recursive: true);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Am")]
    [InlineData("F#m7")]
    [InlineData("Bbmaj7")]
    [InlineData("C#dim")]
    [InlineData("Gsus4")]
    [InlineData("Cadd9")]
    [InlineData("F#m7b5")]
    [InlineData("G/B")]
    [InlineData("Am7/G")]
    [InlineData("C6/9")]
    [InlineData("D(add4)")]
    [InlineData("N.C.")]
    public void Chord_names_are_recognised(string chord) => Assert.True(ChordNotation.IsChord(chord));

    [Theory]
    [InlineData("Chorus")]
    [InlineData("Bridge")]
    [InlineData("Verse 1")]
    [InlineData("x2")]
    [InlineData("00:12.00")]
    [InlineData("ar:Someone")]
    [InlineData("H")]
    [InlineData("Be")]
    [InlineData("")]
    public void Other_words_are_not_chords(string text) => Assert.False(ChordNotation.IsChord(text));

    [Fact]
    public void Chords_leave_the_line_and_remember_their_places()
    {
        var (text, chords) = ChordNotation.Extract("[Am]Hello [F]wor[G/B]ld [Chorus]");
        Assert.Equal("Hello world [Chorus]", text);
        Assert.Equal([new LyricChord(0, "Am"), new LyricChord(6, "F"), new LyricChord(9, "G/B")], chords!);
        Assert.Equal("[Am]Hello [F]wor[G/B]ld [Chorus]", ChordNotation.Inline(text, chords));
    }

    [Fact]
    public void A_lone_bracketed_letter_in_ordinary_lyrics_stays_text()
    {
        var lyrics = LrcParser.Parse("Part [A] of the story\nand more words")!;
        Assert.False(lyrics.HasChords);
        Assert.Equal("Part [A] of the story", lyrics.Lines[0].Text);
    }

    [Fact]
    public void ChordPro_directives_become_title_comments_and_chords()
    {
        const string sheet = "{title: My Song}\n{artist: Someone}\n# a remark\n{c: Intro}\n[C] [G] [Am]\n\n{soc}\n[F]Sing it [C]loud\n{eoc}\n{sot}\ne|--0--|\n{eot}\n{key: C}\n";
        var lyrics = LrcParser.Parse(sheet)!;

        Assert.Equal("My Song", lyrics.Title);
        Assert.Equal("Someone", lyrics.Artist);
        Assert.False(lyrics.Synced);
        Assert.Equal("Intro", lyrics.Lines[0].Text);
        Assert.Equal(["C", "G", "Am"], lyrics.Lines[1].Chords!.Select(c => c.Name));
        Assert.False(lyrics.Lines[1].IsBlank); // chords alone are not a gap
        var sung = lyrics.Lines.Single(l => l.Text == "Sing it loud");
        Assert.Equal([0, 8], sung.Chords!.Select(c => c.At));
        Assert.DoesNotContain(lyrics.Lines, l => l.Text.Contains("e|--") || l.Text.Contains('{'));
    }

    [Fact]
    public void Timed_lines_keep_their_times_and_take_inline_chords()
    {
        var lyrics = LrcParser.Parse("[ar:X]\n[00:05.00][Am]First [F]line\n[00:09.50]Second [C]line")!;
        Assert.True(lyrics.Synced);
        Assert.Equal(TimeSpan.FromSeconds(5), lyrics.Lines[0].Time);
        Assert.Equal("First line", lyrics.Lines[0].Text);
        Assert.Equal([new LyricChord(7, "C")], lyrics.Lines[1].Chords!);
    }

    [Fact]
    public void Chords_survive_saving_as_lrc_or_text()
    {
        var timed = LrcParser.Parse("[00:01.00][Am]One [E]two\n[00:02.00][C]Three")!;
        var again = LrcParser.Parse(timed.ToLrc())!;
        Assert.Equal(timed.Lines.SelectMany(l => l.Chords!), again.Lines.SelectMany(l => l.Chords!));

        var plain = LrcParser.Parse("[Am]One [E]two\n[C]Three")!;
        Assert.Equal("[Am]One [E]two\n[C]Three", plain.ToPlainText());
    }

    [Theory]
    [InlineData("C", 2, false, "D")]
    [InlineData("B", 1, false, "C")]
    [InlineData("C", -1, false, "B")]
    [InlineData("A", 1, false, "A#")]
    [InlineData("A", 1, true, "Bb")]
    [InlineData("F#m7b5", 1, false, "Gm7b5")]
    [InlineData("Bbmaj7", 2, true, "Cmaj7")]
    [InlineData("D/F#", 2, false, "E/G#")]
    [InlineData("Am/G", -2, false, "Gm/F")]
    [InlineData("C6/9", 2, false, "D6/9")]
    [InlineData("Cb", 0, false, "B")]
    [InlineData("N.C.", 5, false, "N.C.")]
    [InlineData("Eb", 12, true, "Eb")]
    public void Chords_transpose(string chord, int by, bool flats, string expected) => Assert.Equal(expected, ChordNotation.Transpose(chord, by, flats));

    [Fact]
    public void The_spelling_follows_the_sheet_unless_chosen()
    {
        Assert.Equal("A#", ChordNotation.Display("A#", 0, ChordSpelling.Auto, sheetUsesFlats: true)); // untouched
        Assert.Equal("Bb", ChordNotation.Display("A#", 0, ChordSpelling.Flats, sheetUsesFlats: false));
        Assert.Equal("Db", ChordNotation.Display("C", 1, ChordSpelling.Auto, sheetUsesFlats: true));
        Assert.Equal("C#", ChordNotation.Display("C", 1, ChordSpelling.Auto, sheetUsesFlats: false));
        Assert.Equal("C#", ChordNotation.Display("C", 1, ChordSpelling.Sharps, sheetUsesFlats: true));
        Assert.True(LrcParser.Parse("[Bb]a [Eb]b [F#]c")!.ChordsUseFlats);
    }

    [Fact]
    public void A_sheet_is_attached_to_the_timed_lines_with_the_same_words()
    {
        var timed = LrcParser.Parse("[00:01.00]Hello, my old friend\n[00:05.00]Something else entirely\n[00:09.00]Hello my old friend")!;
        var sheet = LrcParser.Parse("[Am]Hello my [G]old friend\n[C]Something [F]else entirely")!;

        var merged = ChordNotation.Attach(timed, sheet)!;

        Assert.Equal(timed.Lines.Select(l => l.Time), merged.Lines.Select(l => l.Time));
        Assert.Equal("Hello, my old friend", merged.Lines[0].Text); // the timed lyrics' own words stay
        Assert.Equal("old friend", merged.Lines[0].Text[merged.Lines[0].Chords![1].At..]);
        Assert.NotNull(merged.Lines[2].Chords); // sung again: chords again
    }

    [Fact]
    public void A_sheet_for_other_words_is_not_attached()
    {
        var timed = LrcParser.Parse("[00:01.00]Completely different words\n[00:05.00]Nothing in common")!;
        var sheet = LrcParser.Parse("[Am]Hello my [G]old friend\n[C]Something [F]else")!;
        Assert.Null(ChordNotation.Attach(timed, sheet));
    }

    private MediaItem Track(string title = "Song") => Item(Path.Combine(_music, title + ".mp3"), title, "Artist");

    private LyricsService Service(params ILyricsSource[] sources) =>
        new(new LyricsStore(Path.Combine(_data, "lyrics")), sources) { ChordSource = new SidecarLyricsSource.Chords() };

    [Fact]
    public async Task A_chord_file_next_to_the_track_is_shown_on_its_own_or_on_the_timed_lyrics()
    {
        File.WriteAllText(Path.Combine(_music, "Song.cho"), "{title: Song}\n[Am]Hello my [G]old friend\n");
        var alone = await Service().GetAsync(Track(), TestContext.Current.CancellationToken);
        Assert.True(alone!.HasChords);
        Assert.Equal(LyricsSources.ChordFile, alone.Source);

        File.WriteAllText(Path.Combine(_music, "Song.lrc"), "[00:03.00]Hello my old friend\n");
        var merged = await Service(new SidecarLyricsSource()).GetAsync(Track(), TestContext.Current.CancellationToken);
        Assert.True(merged!.Synced);
        Assert.True(merged.HasChords);
        Assert.Equal(LyricsSources.Sidecar, merged.Source);
        Assert.Equal(LyricsSources.ChordFile, merged.ChordSource);
    }

    [Fact]
    public async Task A_text_file_counts_as_chords_only_when_it_has_them()
    {
        File.WriteAllText(Path.Combine(_music, "Song.txt"), "Just words\nand more words");
        var plain = await Service(new SidecarLyricsSource()).GetAsync(Track(), TestContext.Current.CancellationToken);
        Assert.False(plain!.HasChords);
        Assert.Equal(LyricsSources.Sidecar, plain.Source);

        File.WriteAllText(Path.Combine(_music, "Song.txt"), "[C]Just [G]words\nand more words");
        var chorded = await Service(new SidecarLyricsSource()).GetAsync(Track(), TestContext.Current.CancellationToken);
        Assert.True(chorded!.HasChords);
    }

    [Fact]
    public async Task Own_chords_and_the_transposition_are_saved_per_track()
    {
        File.WriteAllText(Path.Combine(_music, "Song.crd"), "[C]File [G]chords");
        var item = Track();
        var service = Service();
        service.SaveChordsText(item, "[Dm]My [A]chords");
        service.SetTranspose(item, 3);

        var fresh = await Service().GetAsync(item, TestContext.Current.CancellationToken);
        Assert.Equal(LyricsSources.OwnChords, fresh!.Source);
        Assert.Equal("My chords", fresh.Lines[0].Text);
        Assert.Equal(3, fresh.Transpose);
        Assert.Equal("[Dm]My [A]chords", Service().GetChordsText(item));
        Assert.Null(Service().GetChordsText(Track("Other")));

        var again = Service();
        Assert.True(again.DeleteChordsText(item));
        var back = await again.GetAsync(item, TestContext.Current.CancellationToken);
        Assert.Equal("File chords", back!.Lines[0].Text);
        Assert.Equal(3, back.Transpose); // the key stays with the track
    }
}
