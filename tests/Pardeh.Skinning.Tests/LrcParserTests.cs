using System.Globalization;
using Pardeh.Core.LyricsKit;

namespace Pardeh.Skinning.Tests;

public sealed class LrcParserTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Reads_centisecond_stamps()
    {
        var lyrics = LrcParser.Parse("[00:12.34]Hello\n[01:02.05]World")!;
        Assert.True(lyrics.Synced);
        Assert.Equal(2, lyrics.Lines.Count);
        Assert.Equal(Ms(12340), lyrics.Lines[0].Time);
        Assert.Equal("Hello", lyrics.Lines[0].Text);
        Assert.Equal(Ms(62050), lyrics.Lines[1].Time);
    }

    [Fact]
    public void Reads_millisecond_stamps()
    {
        var lyrics = LrcParser.Parse("[00:12.345]Hello")!;
        Assert.Equal(Ms(12345), lyrics.Lines[0].Time);
    }

    [Fact]
    public void Reads_stamps_without_a_fraction()
    {
        var lyrics = LrcParser.Parse("[00:12]Hello\n[1:05]World")!;
        Assert.Equal(Ms(12000), lyrics.Lines[0].Time);
        Assert.Equal(Ms(65000), lyrics.Lines[1].Time);
    }

    [Fact]
    public void A_one_digit_fraction_is_tenths()
    {
        Assert.Equal(Ms(12500), LrcParser.Parse("[00:12.5]x")!.Lines[0].Time);
    }

    [Theory]
    [InlineData("[00:12:34]x", 12340)] // a colon before the fraction
    [InlineData("[00:12,34]x", 12340)] // a comma
    [InlineData("[1:02:03.50]x", 3723500)] // hours
    [InlineData("[75:30.00]x", 4530000)] // minutes past 59
    [InlineData("[ 00:01.00 ]x", 1000)] // spaces inside
    [InlineData("[120:00]x", 7200000)]
    public void Reads_other_stamp_spellings(string line, double expectedMs)
    {
        Assert.Equal(Ms(expectedMs), LrcParser.Parse(line)!.Lines[0].Time);
    }

    [Fact]
    public void Several_stamps_on_one_line_make_several_lines()
    {
        var lyrics = LrcParser.Parse("[00:10.00][00:50.00]Chorus\n[00:30.00]Verse")!;
        Assert.Equal(["Chorus", "Verse", "Chorus"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal([Ms(10000), Ms(30000), Ms(50000)], lyrics.Lines.Select(l => l.Time));
    }

    [Fact]
    public void Reads_the_header_tags()
    {
        var lyrics = LrcParser.Parse("[ar:Some Artist]\n[ti:Some Title]\n[al:Some Album]\n[by:Me]\n[length:03:45]\n[00:01.00]x")!;
        Assert.Equal("Some Artist", lyrics.Artist);
        Assert.Equal("Some Title", lyrics.Title);
        Assert.Equal("Some Album", lyrics.Album);
        Assert.Equal("Me", lyrics.Author);
        Assert.Equal(TimeSpan.FromSeconds(225), lyrics.Length);
        Assert.Single(lyrics.Lines);
    }

    [Fact]
    public void Header_tags_are_case_insensitive_and_ignore_unknown_ones()
    {
        var lyrics = LrcParser.Parse("[AR: Big]\n[Tool:foo]\n[re:player]\n[00:01.00]x")!;
        Assert.Equal("Big", lyrics.Artist);
        Assert.Single(lyrics.Lines);
    }

    [Theory]
    [InlineData("+500", 9500)] // a positive offset shows the lyrics sooner
    [InlineData("-500", 10500)]
    [InlineData("500", 9500)]
    [InlineData("+0", 10000)]
    [InlineData("abc", 10000)]
    public void Applies_the_offset_tag(string offset, double expectedMs)
    {
        var lyrics = LrcParser.Parse($"[offset:{offset}]\n[00:10.00]x")!;
        Assert.Equal(Ms(expectedMs), lyrics.Lines[0].Time);
        Assert.Equal(0, lyrics.OffsetMs); // the file's offset is baked in; OffsetMs is the user's
    }

    [Fact]
    public void The_offset_applies_even_when_the_tag_comes_last()
    {
        var lyrics = LrcParser.Parse("[00:10.00]x\n[offset:+1000]")!;
        Assert.Equal(Ms(9000), lyrics.Lines[0].Time);
    }

    [Fact]
    public void An_offset_never_makes_a_time_negative()
    {
        var lyrics = LrcParser.Parse("[offset:+5000]\n[00:01.00]x\n[00:09.00]y")!;
        Assert.Equal(TimeSpan.Zero, lyrics.Lines[0].Time);
        Assert.Equal(Ms(4000), lyrics.Lines[1].Time);
    }

    [Fact]
    public void Reads_enhanced_word_stamps()
    {
        var lyrics = LrcParser.Parse("[00:10.00]<00:10.00>Hello <00:10.50>big <00:11.20>world<00:12.00>")!;
        var line = lyrics.Lines[0];
        Assert.Equal("Hello big world", line.Text);
        Assert.NotNull(line.Words);
        Assert.Equal(3, line.Words!.Count);
        Assert.Equal("Hello ", line.Words[0].Text);
        Assert.Equal(Ms(10500), line.Words[1].Time);
        Assert.Equal("world", line.Words[2].Text);
        Assert.Equal(Ms(11200), line.Words[2].Time);
    }

    [Fact]
    public void Word_stamps_follow_a_repeated_line()
    {
        var lyrics = LrcParser.Parse("[00:10.00][00:30.00]<00:10.00>a <00:11.00>b")!;
        Assert.Equal(2, lyrics.Lines.Count);
        Assert.Equal(Ms(11000), lyrics.Lines[0].Words![1].Time);
        Assert.Equal(Ms(31000), lyrics.Lines[1].Words![1].Time);
    }

    [Fact]
    public void Word_stamps_follow_the_offset()
    {
        var lyrics = LrcParser.Parse("[offset:+1000]\n[00:10.00]<00:10.00>a <00:11.00>b")!;
        Assert.Equal(Ms(9000), lyrics.Lines[0].Time);
        Assert.Equal(Ms(10000), lyrics.Lines[0].Words![1].Time);
    }

    [Fact]
    public void Angle_brackets_that_are_not_stamps_stay_text()
    {
        var lyrics = LrcParser.Parse("[00:01.00]a <b> c <3")!;
        Assert.Equal("a <b> c <3", lyrics.Lines[0].Text);
        Assert.Null(lyrics.Lines[0].Words);
    }

    [Fact]
    public void Blank_stamped_lines_are_kept_as_gaps()
    {
        var lyrics = LrcParser.Parse("[00:01.00]one\n[00:05.00]\n[00:09.00]two")!;
        Assert.Equal(3, lyrics.Lines.Count);
        Assert.True(lyrics.Lines[1].IsBlank);
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public void Out_of_order_lines_are_sorted_and_ties_keep_the_file_order()
    {
        var lyrics = LrcParser.Parse("[00:20.00]c\n[00:10.00]a\n[00:10.00]b\n[00:05.00]z")!;
        Assert.Equal(["z", "a", "b", "c"], lyrics.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Reads_any_line_ending(string eol)
    {
        var lyrics = LrcParser.Parse("[00:01.00]one" + eol + "[00:02.00]two" + eol + "[00:03.00]three" + eol)!;
        Assert.Equal(["one", "two", "three"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Empty_input_gives_nothing()
    {
        Assert.Null(LrcParser.Parse(null));
        Assert.Null(LrcParser.Parse(""));
        Assert.Null(LrcParser.Parse("  \r\n \n"));
        Assert.Null(LrcParser.Parse("[ar:x]\n[ti:y]"));
        Assert.Null(LrcParser.Parse("[00:01.00]\n[00:02.00]  "));
    }

    [Fact]
    public void Text_without_stamps_is_plain_lyrics()
    {
        var lyrics = LrcParser.Parse("First line\nSecond line\n\nAfter a gap")!;
        Assert.False(lyrics.Synced);
        Assert.Equal(["First line", "Second line", "", "After a gap"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Plain_lyrics_keep_section_headers_and_read_the_header_tags()
    {
        var lyrics = LrcParser.Parse("[ti:Song]\n[Chorus]\nla la\n[Verse 2]\nda da")!;
        Assert.False(lyrics.Synced);
        Assert.Equal("Song", lyrics.Title);
        Assert.Equal(["[Chorus]", "la la", "[Verse 2]", "da da"], lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void In_synced_lyrics_lines_without_a_stamp_are_dropped()
    {
        var lyrics = LrcParser.Parse("junk header\n[00:01.00]one\ntranslated line\n[00:02.00]two")!;
        Assert.True(lyrics.Synced);
        Assert.Equal(["one", "two"], lyrics.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("[00:10.00Hello", "[00:10.00Hello")] // never closed
    [InlineData("[xx:yy]Hello", "[xx:yy]Hello")]
    [InlineData("[]Hello", "[]Hello")]
    [InlineData("[00:10.00]]Hello", "]Hello")]
    [InlineData("[00:10.00][Hello", "[Hello")]
    [InlineData("[00:10.0000]Hello", "[00:10.0000]Hello")] // too many fraction digits
    [InlineData("[00:1x.00]Hello", "[00:1x.00]Hello")]
    public void Malformed_brackets_never_throw(string line, string expectedText)
    {
        // A second, valid line makes the file synced, so a malformed first line is dropped or kept as its text.
        var lyrics = LrcParser.Parse(line + "\n[00:20.00]ok")!;
        if (line.StartsWith("[00:10.00]", StringComparison.Ordinal))
        {
            Assert.Equal(expectedText, lyrics.Lines[0].Text);
            Assert.Equal(Ms(10000), lyrics.Lines[0].Time);
        }
        else
        {
            Assert.Equal(["ok"], lyrics.Lines.Select(l => l.Text));
            var plain = LrcParser.Parse(line)!;
            Assert.False(plain.Synced);
            Assert.Equal(expectedText, plain.Lines[0].Text);
        }
    }

    [Fact]
    public void Brackets_inside_the_text_are_kept()
    {
        var lyrics = LrcParser.Parse("[00:01.00]Hello [world] and (more)")!;
        Assert.Equal("Hello [world] and (more)", lyrics.Lines[0].Text);
    }

    [Fact]
    public void Keeps_at_most_5000_lines()
    {
        var text = string.Join("\n", Enumerable.Range(0, 6000).Select(i => $"[{i / 60:00}:{i % 60:00}.00]line {i}"));
        var lyrics = LrcParser.Parse(text)!;
        Assert.Equal(LyricsLimits.MaxLines, lyrics.Lines.Count);
        Assert.Equal("line 4999", lyrics.Lines[^1].Text);

        var plain = LrcParser.Parse(string.Join("\n", Enumerable.Range(0, 6000).Select(i => "line " + i)))!;
        Assert.Equal(LyricsLimits.MaxLines, plain.Lines.Count);
    }

    [Fact]
    public void A_huge_text_is_cut_and_a_long_line_is_shortened()
    {
        var huge = new string('a', LyricsLimits.MaxBytes * 2);
        var lyrics = LrcParser.Parse(huge)!;
        Assert.Single(lyrics.Lines);
        Assert.Equal(LyricsLimits.MaxLineLength, lyrics.Lines[0].Text.Length);
    }

    [Fact]
    public void Control_characters_and_a_byte_order_mark_are_removed()
    {
        var lyrics = LrcParser.Parse("﻿[00:01.00]a\u0001b\u0000c\td")!;
        Assert.Equal("a b c d", lyrics.Lines[0].Text);
    }

    [Fact]
    public void Reads_persian_lyrics_and_marks_them_right_to_left()
    {
        var lyrics = LrcParser.Parse("[ti:سلام]\n[ar:هنرمند]\n[00:05.00]سلام دنیا\n[00:10.50]این یک آزمایش است\n[00:15.00]Hello world\n[00:20.00]")!;
        Assert.Equal("سلام", lyrics.Title);
        Assert.Equal("هنرمند", lyrics.Artist);
        Assert.Equal(4, lyrics.Lines.Count);
        Assert.Equal(LyricsTextDirection.RightToLeft, lyrics.Lines[0].Direction);
        Assert.Equal(LyricsTextDirection.RightToLeft, lyrics.Lines[1].Direction);
        Assert.Equal(LyricsTextDirection.LeftToRight, lyrics.Lines[2].Direction);
        Assert.Equal(LyricsTextDirection.Neutral, lyrics.Lines[3].Direction);
        Assert.Equal(LyricsTextDirection.RightToLeft, lyrics.Direction);
        Assert.Equal("این یک آزمایش است", lyrics.Lines[1].Text);
    }

    [Theory]
    [InlineData("مرحبا بالعالم", LyricsTextDirection.RightToLeft)]
    [InlineData("שלום עולם", LyricsTextDirection.RightToLeft)]
    [InlineData("Hello", LyricsTextDirection.LeftToRight)]
    [InlineData("123 مرحبا", LyricsTextDirection.RightToLeft)] // digits are not strong
    [InlineData("(Hello) مرحبا", LyricsTextDirection.LeftToRight)] // the first strong letter decides
    [InlineData("... مرحبا", LyricsTextDirection.RightToLeft)]
    [InlineData("۱۲۳", LyricsTextDirection.Neutral)]
    [InlineData("", LyricsTextDirection.Neutral)]
    [InlineData("‏abc", LyricsTextDirection.RightToLeft)] // an explicit mark
    public void Detects_the_direction_of_a_line(string text, LyricsTextDirection expected)
    {
        Assert.Equal(expected, LyricLine.DetectDirection(text));
    }

    [Fact]
    public void IndexAt_finds_the_line_for_a_position()
    {
        var lyrics = LrcParser.Parse("[00:10.00]a\n[00:20.00]b\n[00:30.00]c")!;
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.Zero));
        Assert.Equal(-1, lyrics.IndexAt(Ms(9999)));
        Assert.Equal(0, lyrics.IndexAt(Ms(10000)));
        Assert.Equal(0, lyrics.IndexAt(Ms(19999)));
        Assert.Equal(1, lyrics.IndexAt(Ms(20000)));
        Assert.Equal(2, lyrics.IndexAt(TimeSpan.FromHours(2)));
    }

    [Fact]
    public void IndexAt_gives_the_first_line_of_equal_stamps()
    {
        var lyrics = LrcParser.Parse("[00:10.00]a\n[00:10.00]a translated\n[00:20.00]b")!;
        Assert.Equal(0, lyrics.IndexAt(Ms(10500)));
        Assert.Equal(2, lyrics.IndexAt(Ms(20000)));
    }

    [Fact]
    public void IndexAt_follows_the_users_offset()
    {
        var lyrics = LrcParser.Parse("[00:10.00]a\n[00:20.00]b")!;
        lyrics.OffsetMs = 2000; // the lyrics show 2 s later
        Assert.Equal(-1, lyrics.IndexAt(Ms(11999)));
        Assert.Equal(0, lyrics.IndexAt(Ms(12000)));
        lyrics.OffsetMs = -3000; // 3 s sooner
        Assert.Equal(1, lyrics.IndexAt(Ms(17000)));
        Assert.Equal(Ms(17000), lyrics.TimeOf(1));
    }

    [Fact]
    public void IndexAt_is_fast_on_big_lyrics()
    {
        var lyrics = LrcParser.Parse(string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"[{i / 60:00}:{i % 60:00}.00]l")))!;
        for (var i = 0; i < 5000; i++)
            Assert.Equal(i, lyrics.IndexAt(TimeSpan.FromSeconds(i)));
    }

    [Fact]
    public void FromPlainText_trims_the_edges_and_keeps_stanza_gaps()
    {
        var lyrics = Lyrics.FromPlainText("\n\n  one  \r\ntwo\r\n\r\nthree\n\n\n", "x");
        Assert.False(lyrics.Synced);
        Assert.Equal(["one", "two", "", "three"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal("x", lyrics.Source);
        Assert.Equal("one\ntwo\n\nthree", lyrics.ToPlainText());
    }

    [Fact]
    public void ToLrc_round_trips_synced_lyrics()
    {
        var text = "[ti:Title]\n[ar:Artist]\n[al:Album]\n[by:Me]\n[00:01.00]one\n[00:02.50]\n[01:03.07]three\n";
        var lyrics = LrcParser.Parse(text)!;
        var written = lyrics.ToLrc();
        Assert.Equal(text, written);
        var again = LrcParser.Parse(written)!;
        Assert.Equal(lyrics.Lines.Select(l => (l.Time, l.Text)), again.Lines.Select(l => (l.Time, l.Text)));
    }

    [Fact]
    public void ToLrc_writes_milliseconds_only_when_needed()
    {
        var lyrics = new Lyrics([new LyricLine(Ms(1234), "a"), new LyricLine(Ms(2000), "b")], synced: true);
        Assert.Equal("[00:01.234]a\n[00:02.00]b\n", lyrics.ToLrc());
        Assert.Equal(Ms(1234), LrcParser.Parse(lyrics.ToLrc())!.Lines[0].Time);
    }

    [Fact]
    public void ToLrc_round_trips_word_stamps_and_persian_text()
    {
        var text = "[00:10.00]<00:10.00>سلام <00:10.50>دنیا\n[00:12.00]<00:12.00>Hello <00:13.00>you\n";
        var lyrics = LrcParser.Parse(text)!;
        Assert.Equal(text, lyrics.ToLrc());
    }

    [Fact]
    public void ToLrc_can_bake_the_users_offset_into_the_times()
    {
        var lyrics = LrcParser.Parse("[00:10.00]a")!;
        lyrics.OffsetMs = 1500;
        Assert.Equal("[00:11.50]a\n", lyrics.ToLrc());
        Assert.Equal("[00:10.00]a\n", lyrics.ToLrc(applyOffset: false));
    }

    [Fact]
    public void ToLrc_of_plain_lyrics_is_the_text_with_the_header()
    {
        var lyrics = LrcParser.Parse("[ti:T]\nline 1\nline 2")!;
        Assert.Equal("[ti:T]\nline 1\nline 2\n", lyrics.ToLrc());
        Assert.False(LrcParser.Parse(lyrics.ToLrc())!.Synced);
    }

    [Fact]
    public void Duplicated_stamps_survive_a_round_trip()
    {
        var lyrics = LrcParser.Parse("[00:10.00]a\n[00:10.00]a (translation)\n[00:20.00]b")!;
        var again = LrcParser.Parse(lyrics.ToLrc())!;
        Assert.Equal(["a", "a (translation)", "b"], again.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void Parsing_and_writing_ignore_the_regional_format(string culture)
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var lyrics = LrcParser.Parse("[offset:+250]\n[ti:Title]\n[00:12.34]Hello\n[01:02.5]I\n[length:03:30]")!;
            Assert.Equal(Ms(12090), lyrics.Lines[0].Time);
            Assert.Equal(Ms(62250), lyrics.Lines[1].Time);
            Assert.Equal("Hello", lyrics.Lines[0].Text);
            Assert.Equal("[ti:Title]\n[length:03:30.00]\n[00:12.09]Hello\n[01:02.25]I\n", lyrics.ToLrc());
            // Turkish dotted/dotless i must not change tag names or the track key.
            Assert.Equal("title", LyricsTrackKey.Normalize("TITLE"));
            Assert.Equal("ii", LyricsTrackKey.Normalize("II"));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }

    [Fact]
    public void Reads_a_sample_in_a_real_world_shape()
    {
        const string File = "[ti:Example]\r\n[ar:Someone]\r\n[al:Album]\r\n[by:]\r\n[offset:0]\r\n\r\n[00:00.50]\r\n[00:05.17]First line\r\n[00:09.80]Second [line]\r\n[00:14.36][01:30.00]Refrain\r\n[00:20.00]\r\n";
        var lyrics = LrcParser.Parse(File)!;
        Assert.Equal("Example", lyrics.Title);
        Assert.Equal(6, lyrics.Lines.Count);
        Assert.Equal("Refrain", lyrics.Lines[^1].Text);
        Assert.Equal(Ms(90000), lyrics.Lines[^1].Time);
        Assert.Equal("First line", lyrics.Lines[1].Text);
        Assert.Equal("Second [line]", lyrics.Lines[2].Text);
    }
}
