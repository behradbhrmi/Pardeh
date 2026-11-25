using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

public sealed class SharedLinkTests
{
    [Theory]
    [InlineData("https://host/a.mp4", "https://host/a.mp4")]
    [InlineData("Look at this: https://host/a.mp4?id=1.", "https://host/a.mp4?id=1")]
    [InlineData("A title\r\nhttps://host/v/clip\r\n", "https://host/v/clip")]
    [InlineData("(see <https://host/a.m3u8>)", "https://host/a.m3u8")]
    [InlineData("https://host/Film_(2008).mp4", "https://host/Film_(2008).mp4")]
    [InlineData("rtsp://camera/1 then https://host/b", "rtsp://camera/1")]
    [InlineData("no link here, only file:///c/a.mp4 and https://", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_link_is_found_in_shared_text(string? text, string? expected) => Assert.Equal(expected, NetworkMedia.FindAddress(text));

    [Fact]
    public void A_web_page_is_explained_in_words() =>
        Assert.Contains("web page", MediaPlayer.FriendlyReason("Connecting failed: Invalid data found when processing input"));
}

public sealed class HlsMasterTests
{
    private const string Home = "https://host.example/video/master.m3u8";

    private const string Muxed = """
        #EXTM3U
        #EXT-X-STREAM-INF:BANDWIDTH=2149280,CODECS="mp4a.40.2,avc1.64001f",RESOLUTION=1280x720
        720/list.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=246440,CODECS="mp4a.40.5,avc1.42000d",RESOLUTION=320x184
        240/list.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=836280,CODECS="mp4a.40.2,avc1.64001f",RESOLUTION=848x480
        480/list.m3u8
        #EXT-X-STREAM-INF:BANDWIDTH=6221600,CODECS="mp4a.40.2,avc1.640028",RESOLUTION=1920x1080
        1080/list.m3u8
        """;

    [Theory]
    [InlineData(1080, 1080)]
    [InlineData(720, 720)]
    [InlineData(800, 720)]
    [InlineData(100, 184)] // nothing is small enough: the smallest there is
    public void The_tallest_size_that_fits_is_chosen(int most, int height)
    {
        var choice = HlsMaster.Choose(Home, Muxed, most)!;
        Assert.Equal(height, choice.Height);
    }

    [Fact]
    public void Sound_comes_from_the_smallest_size_with_the_same_kind_of_sound()
    {
        var choice = HlsMaster.Choose(Home, Muxed.ReplaceLineEndings("\r\n"), 1080)!;
        Assert.Equal("https://host.example/video/1080/list.m3u8", choice.Picture);
        // Not the 240 one: its sound is another kind (mp4a.40.5).
        Assert.Equal("https://host.example/video/480/list.m3u8", choice.Sound);
    }

    [Fact]
    public void A_separate_sound_playlist_is_used_and_AAC_and_the_lower_data_rate_win()
    {
        const string list = """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",LANGUAGE="de",NAME="Deutsch",DEFAULT=NO,URI="a/de.m3u8"
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aac",LANGUAGE="en",NAME="English, original",DEFAULT=YES,URI="a/en.m3u8"
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="ac3",LANGUAGE="en",NAME="English",DEFAULT=YES,URI="a/ac3.m3u8"
            #EXT-X-STREAM-INF:BANDWIDTH=8001098,CODECS="avc1.64002a,mp4a.40.2",RESOLUTION=1920x1080,AUDIO="aac"
            v9.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=4943747,CODECS="avc1.64002a,mp4a.40.2",RESOLUTION=1920x1080,AUDIO="aac"
            v7.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=4000000,CODECS="avc1.64002a,ac-3",RESOLUTION=1920x1080,AUDIO="ac3"
            v7ac3.m3u8
            #EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=187492,RESOLUTION=1920x1080,URI="iframes.m3u8"
            """;
        var choice = HlsMaster.Choose(Home, list, 1080)!;
        Assert.Equal("https://host.example/video/v7.m3u8", choice.Picture);
        Assert.Equal("https://host.example/video/a/en.m3u8", choice.Sound);
    }

    [Fact]
    public void A_list_of_sound_only_has_no_picture()
    {
        const string list = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=64000,CODECS="mp4a.40.2"
            low.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=256000,CODECS="mp4a.40.2"
            https://other.example/high.m3u8
            """;
        var choice = HlsMaster.Choose(Home, list, 1080)!;
        Assert.Null(choice.Picture);
        Assert.Equal("https://other.example/high.m3u8", choice.Sound);
    }

    [Fact]
    public void Without_sizes_or_codecs_the_best_data_rate_is_taken()
    {
        var choice = HlsMaster.Choose(Home, "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=100\nlo.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=900\nhi.m3u8\n", 1080)!;
        Assert.Equal("https://host.example/video/hi.m3u8", choice.Picture);
        Assert.Equal(choice.Picture, choice.Sound);
    }

    [Theory]
    [InlineData("#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nseg1.ts\n")] // a list of pieces, not of sizes
    [InlineData("<html>not a playlist</html>")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=100\nfile:///c:/secret.txt\n")] // a list from a server may only name web addresses
    public void Anything_else_is_left_to_the_decoder(string text) => Assert.Null(HlsMaster.Choose(Home, text, 1080));
}
