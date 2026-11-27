using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Skinning.Tests;

public sealed class JunkTextTests
{
    [Theory]
    [InlineData("Song | www.site.com", "Song")]
    [InlineData("Song [MySite.ir]", "Song")]
    [InlineData("Song (http://dl.site.net/a.mp3)", "Song")]
    [InlineData("Song - Site.com", "Song")]
    [InlineData("site.org - Song", "Song")]
    [InlineData("Song @musicchannel", "Song")]
    [InlineData("Song [128kbps]", "Song")]
    [InlineData("Song (320)", "Song")]
    [InlineData("Song - 320", "Song")]
    [InlineData("Song 320 kbps", "Song")]
    [InlineData("Song (Download from BestSite.com)", "Song")]
    [InlineData("Song - Downloaded From mysite", "Song")]
    [InlineData("Song [Exclusive On Music-Fa.com]", "Song")]
    [InlineData("Song (Remix) [www.site.com]", "Song (Remix)")]
    [InlineData("Artist - Song (Live) | t.me/channel", "Artist - Song (Live)")]
    [InlineData("Song nicemusic.me", "Song")]
    [InlineData("دانلود آهنگ جدید عشق من", "عشق من")]
    [InlineData("عشق من | دانلود از سایت موزیک", "عشق من")]
    [InlineData("عشق من (۳۲۰)", "عشق من")]
    [InlineData("عشق من - www.ahang.ir", "عشق من")]
    [InlineData("کانال تلگرام ما", "")]
    [InlineData("www.site.com", "")]
    public void Site_junk_is_removed_with_what_held_it(string input, string expected)
    {
        var result = JunkText.Clean(input);

        Assert.True(result.Changed);
        Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("will.i.am")]
    [InlineData("R.E.M.")]
    [InlineData("St.Germain")]
    [InlineData("Call.Me")]
    [InlineData("Mr. Big")]
    [InlineData("Track No. 5")]
    [InlineData("Studio 128")]
    [InlineData("320")]
    [InlineData("Route 66 (1966)")]
    [InlineData("Song (Remix) [Radio Edit]")]
    [InlineData("AC/DC - T.N.T.")]
    [InlineData("M@rk")]
    [InlineData("Love @ First")]
    [InlineData("Download Festival")]
    [InlineData("N.W.A - Straight Outta Compton")]
    [InlineData("Part I - The End...")]
    [InlineData("  Spaced   Title  ")]
    [InlineData("دانلود")]
    [InlineData("ترانه‌ی بی‌کلام")]
    [InlineData("node.js")]
    public void Real_names_are_left_exactly_as_they_are(string input)
    {
        var result = JunkText.Clean(input);

        Assert.False(result.Changed, string.Join(" | ", result.Found));
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void The_users_junk_phrases_are_removed_and_kept_phrases_survive()
    {
        var rules = new JunkRules(["پخش اختصاصی تهران", "BestBeats"], ["Radio.com"]);

        Assert.Equal("ترانه", JunkText.Clean("ترانه - پخش اختصاصي تهران", rules).Text); // Arabic yeh in the file
        Assert.Equal("Song", JunkText.Clean("Song [bestbeats]", rules).Text);
        Assert.False(JunkText.Clean("Live at Radio.com", rules).Changed);
        Assert.Equal("Live at Radio.com", JunkText.Clean("Live at Radio.com | other.com", rules).Text);
    }

    [Theory]
    [InlineData("Ø³Ù„Ø§Ù… Ø¯Ù†ÛŒØ§", "سلام دنیا")] // UTF-8 read as Windows-1252
    [InlineData("ÓáÇã", "سلام")] // Windows-1256 read as Windows-1252
    public void Broken_persian_text_is_repaired(string broken, string expected) => Assert.Equal(expected, Mojibake.Repair(broken));

    [Theory]
    [InlineData("Françoise Hardy")]
    [InlineData("Sigur Rós")]
    [InlineData("Ænima")]
    [InlineData("ÀÉÎ")]
    [InlineData("سلام دنیا")]
    [InlineData("Мой голос")]
    [InlineData("東京")]
    [InlineData("Song")]
    public void Real_text_is_not_taken_for_broken(string text) => Assert.Null(Mojibake.Repair(text));

    [Theory]
    [InlineData("01 - Artist Name - Track Name (site.com).mp3", "Artist Name", "Track Name", 1)]
    [InlineData("128_site.com_Googoosh - Track.mp3", "Googoosh", "Track", 0)]
    [InlineData("Artist_Name_-_Track_Name.mp3", "Artist Name", "Track Name", 0)]
    [InlineData("Artist-Melody.mp3", "Artist", "Melody", 0)]
    [InlineData("Artist - Song.mp3", "Artist", "Song", 0)]
    [InlineData("Track 03.mp3", null, null, 0)]
    [InlineData("Artist - Album - Title.flac", "Artist", "Title", 0)]
    [InlineData("07. Just A Title.mp3", null, "Just A Title", 7)]
    [InlineData("track01.mp3", null, null, 0)]
    [InlineData("AUD-20250101-WA0003.mp3", null, null, 0)]
    public void Artist_and_title_are_read_from_a_file_name(string name, string? artist, string? title, int track)
    {
        var guess = FileNameReader.Read(name);

        Assert.Equal((artist, title, (uint)track), (guess.Artist, guess.Title, guess.Track));
    }

    private static TagFields Tags(string title, string artist = "Artist", string album = "Album", int year = 1975, int track = 3) =>
        new() { Title = title, Artists = artist.Length > 0 ? [artist] : [], Album = album, Year = (uint)year, Track = (uint)track, AlbumArtists = [], Genres = [] };

    [Theory]
    [InlineData("{artist} - {title}", "Artist - Song.mp3")]
    [InlineData("{track} - {title}", "03 - Song.mp3")]
    [InlineData("{artist} - {album} - {track} - {title}", "Artist - Album - 03 - Song.mp3")]
    [InlineData("{title} ({year}) [{genre}]", "Song (1975).mp3")]
    public void A_pattern_names_a_file(string pattern, string expected) => Assert.Equal(expected, NamePattern.Apply(pattern, Tags("Song"), ".mp3"));

    [Theory]
    [InlineData("{artist} - {title}", "Song.mp3")]
    [InlineData("{title} - {artist}", "Song.mp3")]
    [InlineData("{artist} - {album} - {track} - {title}", "Song.mp3")]
    [InlineData("{title} ({artist})", "Song.mp3")]
    [InlineData("{artist}_{title}", "Song.mp3")]
    public void Empty_fields_leave_no_stray_dashes_or_brackets(string pattern, string expected) =>
        Assert.Equal(expected, NamePattern.Apply(pattern, Tags("Song", "", "", 0, 0), ".mp3"));

    [Fact]
    public void Names_are_made_safe()
    {
        Assert.Equal("AC-DC - What - 'Now' (1).mp3", NamePattern.Apply("{artist} - {title}", Tags("What? : \"Now\" <1>", "AC/DC"), ".mp3")!.Replace("What -", "What -"));
        Assert.Equal("CON_.mp3", NamePattern.Apply("{title}", Tags("CON"), ".mp3"));
        Assert.Null(NamePattern.Apply("{artist} - {title}", Tags(""), ".mp3"));
        Assert.Null(NamePattern.Apply("{title}", Tags("..."), ".mp3"));
        var longName = NamePattern.Apply("{title}", Tags(new string('آ', 300)), ".mp3")!;
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(longName) <= 220);
        Assert.EndsWith(".mp3", longName);
    }

    [Theory]
    [InlineData("{artist}", "title")]
    [InlineData("{title}/{artist}", "can't contain")]
    [InlineData("{title} {composer}", "no field")]
    [InlineData("", "Type")]
    public void A_pattern_that_cannot_be_used_says_why(string pattern, string part) => Assert.Contains(part, NamePattern.Check(pattern));
}
