using System.Net;
using System.Text;
using System.Text.Json;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.OnlineInfo;

namespace Pardeh.Skinning.Tests;

/// <summary>Made-up database entries and a server that answers as told.</summary>
internal static class OnlineData
{
    public const string RecordingId = "11111111-1111-1111-1111-111111111111";
    public const string ReleaseId = "22222222-2222-2222-2222-222222222222";
    public const string GroupId = "33333333-3333-3333-3333-333333333333";

    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8];

    public static MetadataRelease Release(string title = "Morning Album", string? date = "1999-04-02", string? primary = "Album", string[]? secondary = null,
        bool official = true, uint track = 3, string albumArtist = "Some Singer", string? group = GroupId, string id = ReleaseId) =>
        new(id, group, title, albumArtist, date, primary, secondary ?? [], official, track);

    public static MetadataCandidate Candidate(string title = "Quiet River", string artist = "Some Singer", int seconds = 200, string disambiguation = "",
        string? first = "1999-04-02", string[]? aliases = null, params MetadataRelease[] releases) =>
        new(RecordingId, title, disambiguation, [new ArtistCredit(artist, aliases ?? [])], artist, seconds * 1000, first,
            releases.Length > 0 ? releases : [Release()]);

    public static LocalSong Song(string title = "Quiet River", string artist = "Some Singer", double seconds = 200, string album = "", string hints = "") =>
        new(title, artist.Length > 0 ? [artist] : [], album, seconds, hints);

    public static string Answer(string title = "Quiet River", string artist = "Some Singer", int length = 200000) => JsonSerializer.Serialize(new
    {
        count = 1,
        recordings = new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = RecordingId, ["score"] = 100, ["title"] = title, ["length"] = length, ["first-release-date"] = "1999-04-02",
                ["artist-credit"] = new object[]
                {
                    new Dictionary<string, object?> { ["name"] = artist, ["joinphrase"] = " feat. ", ["artist"] = new { name = artist, aliases = new[] { new { name = "خواننده" } } } },
                    new Dictionary<string, object?> { ["name"] = "Guest", ["artist"] = new { name = "Guest" } },
                },
                ["releases"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = ReleaseId, ["title"] = "Morning\u0007 Album", ["status"] = "Official", ["date"] = "1999-04-02",
                        ["artist-credit"] = new[] { new { name = "Various Artists" } },
                        ["release-group"] = new Dictionary<string, object?> { ["id"] = GroupId, ["primary-type"] = "Album", ["secondary-types"] = new[] { "Compilation" } },
                        ["media"] = new[] { new Dictionary<string, object?> { ["position"] = 1, ["track-offset"] = 6, ["track"] = new[] { new { number = "A7" } } } },
                    },
                },
            },
        },
    });
}

internal sealed class FakeWeb : HttpMessageHandler
{
    public readonly List<(string Address, string Agent)> Asked = [];

    public Func<Uri, int, HttpResponseMessage> Reply { get; set; } = (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Asked.Add((request.RequestUri!.AbsoluteUri, request.Headers.UserAgent.ToString()));
        return Task.FromResult(Reply(request.RequestUri, Asked.Count));
    }

    public static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    public static HttpResponseMessage Moved(string to) => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri(to, UriKind.RelativeOrAbsolute) } };

    public static HttpResponseMessage Busy(int seconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }
}

public sealed class MusicBrainzProviderTests
{
    private const string Agent = "Pardeh/9.9.9 (https://example.org/project)";

    private readonly FakeWeb _web = new();
    private readonly List<TimeSpan> _waits = [];

    private MusicBrainzProvider Provider() => new(new HttpClient(_web), Agent, throttle: new RequestThrottle(TimeSpan.Zero),
        delay: (wait, _) =>
        {
            _waits.Add(wait);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task A_search_sends_only_the_title_and_artist_with_the_agent()
    {
        _web.Reply = (_, _) => FakeWeb.Ok(OnlineData.Answer());

        await Provider().SearchAsync(new MetadataSearch("Quiet \"River\" \\ 2", "Some Singer"), TestContext.Current.CancellationToken);

        var (address, agent) = Assert.Single(_web.Asked);
        Assert.Equal(Agent, agent);
        var sent = new Uri(address);
        Assert.Equal("musicbrainz.org", sent.Host);
        Assert.Equal("?fmt=json&limit=100&query=" + "recording:\"Quiet \\\"River\\\" \\\\ 2\" AND artist:\"Some Singer\" AND NOT comment:live",
            "?fmt=json&limit=100&query=" + Uri.UnescapeDataString(sent.Query.Split("query=")[1]));
    }

    [Fact]
    public async Task A_live_song_and_a_title_only_search_are_asked_as_such()
    {
        _web.Reply = (_, _) => FakeWeb.Ok(OnlineData.Answer());

        await Provider().SearchAsync(new MetadataSearch("Quiet River", null, IncludeLive: true), TestContext.Current.CancellationToken);

        Assert.Equal("recording:\"Quiet River\"", Uri.UnescapeDataString(_web.Asked[0].Address.Split("query=")[1]));
    }

    [Fact]
    public async Task The_answer_is_read_with_artists_releases_and_track()
    {
        _web.Reply = (_, _) => FakeWeb.Ok(OnlineData.Answer());

        var found = Assert.Single(await Provider().SearchAsync(new MetadataSearch("Quiet River", "Some Singer"), TestContext.Current.CancellationToken));

        Assert.Equal("Quiet River", found.Title);
        Assert.Equal(["Some Singer", "Guest"], found.Artists.Select(a => a.Name));
        Assert.Equal("Some Singer feat. Guest", found.ArtistText);
        Assert.Equal(["خواننده"], found.Artists[0].OtherNames);
        Assert.Equal(200000, found.LengthMs);
        Assert.Equal(1999u, found.FirstYear);
        var release = Assert.Single(found.Releases);
        Assert.Equal("Morning Album", release.Title); // the control character is gone
        Assert.Equal("Various Artists", release.AlbumArtist);
        Assert.Equal(["Compilation"], release.SecondaryTypes);
        Assert.Equal(7u, release.Track); // "A7" is no number: the place on the disc
    }

    [Fact]
    public async Task A_busy_service_is_asked_again_after_the_time_it_names()
    {
        _web.Reply = (_, n) => n == 1 ? FakeWeb.Busy(7) : FakeWeb.Ok(OnlineData.Answer());

        var found = await Provider().SearchAsync(new MetadataSearch("Quiet River", "Some Singer"), TestContext.Current.CancellationToken);

        Assert.Single(found);
        Assert.Equal([TimeSpan.FromSeconds(7)], _waits);
    }

    [Fact]
    public async Task A_service_that_stays_busy_or_cannot_be_reached_says_so()
    {
        _web.Reply = (_, _) => FakeWeb.Busy(1);
        var busy = await Assert.ThrowsAsync<MetadataException>(() => Provider().SearchAsync(new MetadataSearch("a", "b"), TestContext.Current.CancellationToken));
        Assert.Contains("busy", busy.Message);
        Assert.Equal(3, _web.Asked.Count);

        _web.Reply = (_, _) => throw new HttpRequestException("no network");
        var offline = await Assert.ThrowsAsync<MetadataException>(() => Provider().SearchAsync(new MetadataSearch("a", "b"), TestContext.Current.CancellationToken));
        Assert.Contains("couldn't be reached", offline.Message);
    }

    [Fact]
    public async Task An_answer_that_is_no_json_is_an_error()
    {
        _web.Reply = (_, _) => FakeWeb.Ok("<html>");

        await Assert.ThrowsAsync<MetadataException>(() => Provider().SearchAsync(new MetadataSearch("a", "b"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_cover_is_followed_to_the_archive_without_the_contact_agent()
    {
        _web.Reply = (address, _) => address.Host switch
        {
            "coverartarchive.org" => FakeWeb.Moved("https://archive.org/download/x/y.jpg"),
            "archive.org" => FakeWeb.Moved("https://dn1.ca.archive.org/0/items/x/y.jpg"),
            _ => FakeWeb.Bytes(OnlineData.Jpeg),
        };

        var cover = await Provider().CoverAsync(OnlineData.Release(), TestContext.Current.CancellationToken);

        Assert.Equal("image/jpeg", cover!.MimeType);
        Assert.Equal(OnlineData.Jpeg, cover.Data);
        Assert.Equal($"https://coverartarchive.org/release-group/{OnlineData.GroupId}/front-1200", _web.Asked[0].Address);
        Assert.All(_web.Asked, a => Assert.Equal("Pardeh/1.0", a.Agent));
    }

    [Theory]
    [InlineData("https://archive.org.evil.example/x.jpg")]
    [InlineData("https://evilarchive.org/x.jpg")]
    [InlineData("http://archive.org/x.jpg")]
    [InlineData("https://archive.org:8443/x.jpg")]
    [InlineData("https://user@archive.org/x.jpg")]
    [InlineData("file:///c:/x.jpg")]
    public async Task A_cover_pointing_anywhere_else_is_not_fetched(string elsewhere)
    {
        _web.Reply = (address, _) => address.Host == "coverartarchive.org" ? FakeWeb.Moved(elsewhere) : FakeWeb.Bytes(OnlineData.Jpeg);

        await Assert.ThrowsAsync<MetadataException>(() => Provider().CoverAsync(OnlineData.Release(), TestContext.Current.CancellationToken));

        Assert.Single(_web.Asked);
    }

    [Fact]
    public async Task Without_a_cover_every_size_and_place_is_tried_and_other_content_is_refused()
    {
        _web.Reply = (_, n) => n == 1 ? FakeWeb.Bytes("<html>not a picture</html>"u8.ToArray()) : new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Null(await Provider().CoverAsync(OnlineData.Release(), TestContext.Current.CancellationToken));

        Assert.Equal(4, _web.Asked.Count);
    }

    [Fact]
    public async Task An_id_that_is_no_id_never_reaches_an_address()
    {
        Assert.Null(await Provider().CoverAsync(OnlineData.Release(group: "../../x", id: "y?z"), TestContext.Current.CancellationToken));

        Assert.Empty(_web.Asked);
    }

    [Fact]
    public async Task A_huge_cover_is_refused()
    {
        _web.Reply = (_, _) => FakeWeb.Bytes(new byte[11 * 1024 * 1024]);

        await Assert.ThrowsAsync<MetadataException>(() => Provider().CoverAsync(OnlineData.Release(), TestContext.Current.CancellationToken));
    }

    /// <summary>Asks the real services for a famous song. Off unless PARDEH_TEST_ONLINE is set.</summary>
    [Fact]
    public async Task The_real_services_answer_as_expected()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("PARDEH_TEST_ONLINE") is not { Length: > 0 }, "set PARDEH_TEST_ONLINE to ask the real services");
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
        var lookup = new MetadataLookup(new MusicBrainzProvider(http, "Pardeh/1.0 (https://github.com/behradbhrmi/Pardeh)"));
        var song = new LocalSong("Bohemian Rhapsody", ["Queen"], "", 355);

        var result = await lookup.FindAsync([new LookupStep(new MetadataSearch("Bohemian Rhapsody", "Queen"), song)], TestContext.Current.CancellationToken);

        var best = result.Matches[0];
        Assert.Equal(MatchLevel.Strong, best.Level);
        var release = MetadataMatcher.Releases(best.Candidate, "")[0];
        TestContext.Current.TestOutputHelper?.WriteLine($"{best.Candidate.Title} - {best.Candidate.ArtistText} ({best.Candidate.FirstYear}), {release.Title} #{release.Track}: {best.Reason}");
        Assert.Equal("A Night at the Opera", release.Title);
        Assert.Equal(1975u, best.Candidate.FirstYear);
        var cover = await lookup.Provider.CoverAsync(release, TestContext.Current.CancellationToken);
        Assert.True(cover is { Data.Length: > 1000 });
    }
}

public sealed class MetadataMatcherTests
{
    private static MetadataMatch Match(LocalSong song, MetadataCandidate candidate) => MetadataMatcher.Match(song, candidate);

    [Fact]
    public void Title_artist_and_length_make_a_strong_match()
    {
        var match = Match(OnlineData.Song(seconds: 201.4), OnlineData.Candidate());

        Assert.Equal(MatchLevel.Strong, match.Level);
        Assert.Equal("Title and artist match, length within 2 s", match.Reason);
    }

    [Theory]
    [InlineData(0, "can't be compared")]
    [InlineData(213, "differs by 13 s")]
    public void Without_the_length_text_is_never_strong_but_stays_in_sight(double seconds, string reason)
    {
        var match = Match(OnlineData.Song(seconds: seconds), OnlineData.Candidate());

        Assert.Equal(MatchLevel.Possible, match.Level);
        Assert.Contains(reason, match.Reason);
    }

    [Theory]
    [InlineData("Quiet River (Remix)", "", "Quiet River", "", false)]
    [InlineData("Quiet River", "", "Quiet River", "live, 2001: Somewhere", false)]
    [InlineData("Quiet River", "", "Quiet River", "karaoke version", false)]
    [InlineData("Quiet River", "x - Quiet River (Instrumental).mp3", "Quiet River", "", false)]
    [InlineData("Quiet River - Remastered 2011", "", "Quiet River", "", false)]
    [InlineData("Quiet River (Radio Edit)", "", "Quiet River (radio edit)", "", true)]
    [InlineData("Quiet River (Original Mix)", "", "Quiet River", "", true)]
    [InlineData("Quiet River ریمیکس", "", "Quiet River (Remix)", "", true)]
    public void The_versions_must_agree(string title, string file, string found, string disambiguation, bool agree)
    {
        var match = Match(OnlineData.Song(title, hints: file), OnlineData.Candidate(found, disambiguation: disambiguation));

        Assert.Equal(agree ? MatchLevel.Strong : MatchLevel.Possible, match.Level);
        Assert.Equal(agree, match.SameVersion);
    }

    [Theory]
    [InlineData("Let It Live")]
    [InlineData("The Mix")]
    [InlineData("Quiet River (Part 2)")]
    public void Ordinary_words_name_no_version(string title) => Assert.Empty(MetadataMatcher.SplitTitle(title).Versions);

    [Theory]
    [InlineData("Quiet River (feat. Guest)", "Quiet River")]
    [InlineData("Quiet River ft. Guest", "Quiet River")]
    [InlineData("Quiet River [Remastered 2011]", "Quiet River")]
    [InlineData("Quiet River - Live", "Quiet River")]
    [InlineData("(Remix)", "(Remix)")]
    public void Additions_come_off_the_title(string title, string expected) => Assert.Equal(expected, MetadataMatcher.SplitTitle(title).Base);

    [Fact]
    public void Persian_spellings_and_half_spaces_compare_the_same()
    {
        var song = OnlineData.Song("می‌روم", "علي");

        Assert.Equal(MatchLevel.Strong, Match(song, OnlineData.Candidate("میروم", "علی")).Level);
    }

    [Fact]
    public void An_alias_finds_the_artist_written_in_another_script()
    {
        var match = Match(OnlineData.Song("رود آرام", "خواننده"), OnlineData.Candidate("رود آرام", "Some Singer", aliases: ["خواننده"]));

        Assert.Equal(MatchLevel.Strong, match.Level);
    }

    [Theory]
    [InlineData("Some Singer & Guest")]
    [InlineData("Some Singer feat. Guest")]
    [InlineData("Guest, Some Singer")]
    [InlineData("The Some Singer")]
    public void One_of_several_artists_is_enough(string artist) =>
        Assert.Equal(MatchLevel.Strong, Match(OnlineData.Song(artist: artist), OnlineData.Candidate()).Level);

    [Fact]
    public void Another_artist_is_at_most_possible_and_only_with_the_same_length()
    {
        Assert.Equal(MatchLevel.Possible, Match(OnlineData.Song(artist: "Quite Another"), OnlineData.Candidate()).Level);
        Assert.Equal(MatchLevel.None, Match(OnlineData.Song(artist: "Quite Another", seconds: 100), OnlineData.Candidate()).Level);
        Assert.Equal(MatchLevel.None, Match(OnlineData.Song("Loud Mountain"), OnlineData.Candidate()).Level);
    }

    [Fact]
    public void A_small_typing_difference_is_close_not_the_same()
    {
        var match = Match(OnlineData.Song("Quiet Rivver"), OnlineData.Candidate());

        Assert.Equal(Likeness.Close, match.Title);
        Assert.Equal(MatchLevel.Possible, match.Level);
    }

    [Fact]
    public void Among_equals_the_recording_on_the_most_releases_comes_first()
    {
        var obscure = OnlineData.Candidate() with { Id = "obscure" };
        var known = OnlineData.Candidate(releases: [OnlineData.Release(), OnlineData.Release("Best Of", group: null)]) with { Id = "known" };
        var longer = OnlineData.Candidate(seconds: 230, releases: [OnlineData.Release(), OnlineData.Release("B", group: null), OnlineData.Release("C", group: null)]);

        var ranked = MetadataMatcher.Rank(OnlineData.Song(), [longer, obscure, known]);

        Assert.Equal(["known", "obscure", OnlineData.RecordingId], ranked.Select(m => m.Candidate.Id));
    }

    [Fact]
    public void Albums_come_before_compilations_and_the_songs_own_album_before_all()
    {
        var candidate = OnlineData.Candidate(releases:
        [
            OnlineData.Release("Hits", "1998", secondary: ["Compilation"], group: "a"),
            OnlineData.Release("Reissue Album", "2010", group: "b"),
            OnlineData.Release("The Single", "1997", "Single", group: "c"),
            OnlineData.Release("First Album", "1999", group: "d"),
            OnlineData.Release("First Album", "2005", group: "d"),
            OnlineData.Release("Bootleg", "1990", official: false, group: "e"),
        ]);

        Assert.Equal(["First Album", "Reissue Album", "The Single", "Hits", "Bootleg"], MetadataMatcher.Releases(candidate, "").Select(r => r.Title));
        Assert.Equal("1999", MetadataMatcher.Releases(candidate, "")[0].Date);
        Assert.Equal("Hits", MetadataMatcher.Releases(candidate, "hits")[0].Title);
    }
}

public sealed class MetadataLookupTests
{
    private sealed class Database(Func<MetadataSearch, IReadOnlyList<MetadataCandidate>> answer) : IMetadataProvider
    {
        public readonly List<MetadataSearch> Searches = [];

        public string Name => "Test";

        public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel)
        {
            Searches.Add(search);
            return Task.FromResult(answer(search));
        }

        public Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel) => Task.FromResult<CoverImage?>(null);
    }

    private static IReadOnlyList<LookupStep> Steps(string file, string title, string artist, double seconds = 200)
    {
        var tags = new TagFields
        {
            Title = title, Artists = artist.Length > 0 ? [artist] : [], AlbumArtists = [], Album = "", Genres = [], Composers = [], Comment = "", Copyright = "", Year = 0, Track = 0,
        };
        var local = CleanupAnalyzer.Analyze(new TrackFacts("/m/" + file, file, tags, null));
        return MetadataLookup.Steps(tags, local, seconds);
    }

    [Fact]
    public void Searches_use_the_cleaned_tags_then_the_file_name_then_swapped_then_the_title_alone()
    {
        var steps = Steps("01 - File Singer - File Song (best-music.ir).mp3", "Tag Song | www.best-music.ir", "Tag Singer");

        Assert.Equal(
            [
                new MetadataSearch("Tag Song", "Tag Singer"),
                new MetadataSearch("File Song", "File Singer"),
                new MetadataSearch("Tag Singer", "Tag Song"),
                new MetadataSearch("Tag Song", null),
            ], steps.Select(s => s.Search));
    }

    [Fact]
    public void Nothing_but_title_and_artist_words_is_ever_searched()
    {
        var steps = Steps("128_site.com_Folder Secret.mp3", "Quiet River (feat. Guest) [320]", "Some Singer");

        Assert.All(steps, s =>
        {
            Assert.DoesNotContain(".mp3", s.Search.Title + s.Search.Artist);
            Assert.DoesNotContain("site.com", s.Search.Title + s.Search.Artist);
            Assert.DoesNotContain("320", s.Search.Title + s.Search.Artist);
        });
        Assert.Equal(new MetadataSearch("Quiet River", "Some Singer"), steps[0].Search);
    }

    [Fact]
    public void A_song_without_any_name_has_nothing_to_search() => Assert.Empty(Steps("Track 01.mp3", "Track 01", ""));

    [Fact]
    public async Task It_stops_at_the_first_search_that_matches_and_asks_nothing_twice()
    {
        var database = new Database(s => s.Title == "File Song" ? [OnlineData.Candidate("File Song", "File Singer")] : [OnlineData.Candidate("Unrelated", "Nobody", 90)]);
        var lookup = new MetadataLookup(database);
        var steps = Steps("File Singer - File Song.mp3", "Wrong Words", "Wrong Person");

        var result = await lookup.FindAsync(steps, TestContext.Current.CancellationToken);
        await lookup.FindAsync(steps, TestContext.Current.CancellationToken);

        Assert.Equal(MatchLevel.Strong, Assert.Single(result.Matches).Level);
        Assert.Equal(["Wrong Words", "File Song"], database.Searches.Select(s => s.Title));
    }

    [Fact]
    public async Task Swapped_tags_are_found()
    {
        var database = new Database(s => s.Title == "Quiet River" && s.Artist == "Some Singer" ? [OnlineData.Candidate()] : []);

        var result = await new MetadataLookup(database).FindAsync(Steps("x.mp3", "Some Singer", "Quiet River"), TestContext.Current.CancellationToken);

        Assert.Equal(MatchLevel.Strong, result.Matches[0].Level);
    }

    [Fact]
    public async Task A_search_by_hand_lists_poor_matches_too()
    {
        var database = new Database(_ => [OnlineData.Candidate("Unrelated", "Nobody", 90)]);

        var result = await new MetadataLookup(database).SearchAsync("Typed", "Words", OnlineData.Song(), TestContext.Current.CancellationToken);

        Assert.Equal(MatchLevel.None, Assert.Single(result.Matches).Level);
        Assert.Equal(new MetadataSearch("Typed", "Words"), result.Searched);
    }
}

public sealed class OnlineSuggestionTests
{
    private static TagFields Tags(string title = "", string artist = "", string album = "", uint year = 0, uint track = 0, params TagPicture[] pictures) => new()
    {
        Title = title, Artists = artist.Length > 0 ? [artist] : [], AlbumArtists = [], Album = album, Genres = [], Composers = [], Comment = "", Copyright = "",
        Year = year, Track = track, Pictures = pictures, Texts = [],
    };

    private static CleanupProposal Local(TagFields tags, LibrarySignals? signals = null, string? cover = null) =>
        CleanupAnalyzer.Analyze(new TrackFacts("/m/x.mp3", "x.mp3", tags with { Pictures = null }, cover), signals);

    private static MetadataMatch Matched(MatchLevel level, MetadataCandidate? candidate = null) =>
        new(candidate ?? OnlineData.Candidate(), level, "", Likeness.Same, Likeness.Same, true, 0, false);

    private static CleanupSuggestion Of(CleanupProposal proposal, CleanupField field) => Assert.Single(proposal.Suggestions, s => s.Field == field);

    [Fact]
    public void A_strong_match_ticks_empty_and_junk_fields_and_leaves_good_ones()
    {
        var tags = Tags("Quiet River | www.best-music.ir", "some singer", year: 2015);

        var merged = OnlineSuggestions.Merge(Local(tags), tags, Matched(MatchLevel.Strong), OnlineData.Release(), null, false);

        Assert.True(Of(merged, CleanupField.Title).Selected);
        Assert.True(Of(merged, CleanupField.Album).Selected);
        Assert.True(Of(merged, CleanupField.Track).Selected);
        Assert.False(Of(merged, CleanupField.Artists).Selected); // good already, only written differently
        Assert.False(Of(merged, CleanupField.Year).Selected);
        Assert.Equal("1999", Of(merged, CleanupField.Year).Suggested);
    }

    [Fact]
    public void A_possible_match_ticks_nothing_and_keeps_the_local_fixes()
    {
        var tags = Tags("Quiet River | www.best-music.ir", "Some Singer");
        var candidate = OnlineData.Candidate("Quiet River (Live)");

        var merged = OnlineSuggestions.Merge(Local(tags), tags, Matched(MatchLevel.Possible, candidate), OnlineData.Release(), null, false);

        var title = Of(merged, CleanupField.Title);
        Assert.True(title.Selected);
        Assert.Equal("Quiet River", title.Suggested);
        Assert.Equal("Quiet River (Live)", title.Online);
        Assert.DoesNotContain(merged.Suggestions, s => s.Reason == CleanupReason.Online && s.Selected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_name_in_another_script_stays_unless_the_user_chose_the_database(bool chosen)
    {
        var tags = Tags("رود آرام | www.best-music.ir", "خواننده");

        var merged = OnlineSuggestions.Merge(Local(tags), tags, Matched(MatchLevel.Strong), OnlineData.Release(), null, chosen);

        var title = Of(merged, CleanupField.Title);
        Assert.True(title.Selected);
        Assert.Equal(chosen ? "Quiet River" : "رود آرام", title.Suggested);
        Assert.Equal("Quiet River", chosen ? title.Suggested : title.Online);
        Assert.Equal(chosen ? "رود آرام" : null, title.Alternative);
        Assert.Equal(chosen, Of(merged, CleanupField.Artists).Selected);
        // An empty album of a Persian song isn't filled in Latin letters by itself either.
        Assert.Equal(chosen, Of(merged, CleanupField.Album).Selected);
    }

    [Fact]
    public void A_missing_cover_is_ticked_a_good_one_is_not()
    {
        var found = new CoverImage(OnlineData.Jpeg, "image/jpeg");
        var bare = Tags("Quiet River", "Some Singer");
        var covered = bare with { Pictures = [new TagPicture("FrontCover", "image/png", "", [1, 2, 3])] };

        Assert.True(Of(OnlineSuggestions.Merge(Local(bare), bare, Matched(MatchLevel.Strong), OnlineData.Release(), found, false), CleanupField.Cover).Selected);
        Assert.False(Of(OnlineSuggestions.Merge(Local(covered), covered, Matched(MatchLevel.Strong), OnlineData.Release(), found, false), CleanupField.Cover).Selected);
        Assert.False(Of(OnlineSuggestions.Merge(Local(bare), bare, Matched(MatchLevel.Possible), OnlineData.Release(), found, false), CleanupField.Cover).Selected);
    }

    [Fact]
    public void A_suspected_cover_is_replaced_by_a_strong_match_and_only_it_and_the_front()
    {
        var logo = new TagPicture("Other", "image/jpeg", "", [9, 9, 9]);
        var back = new TagPicture("BackCover", "image/jpeg", "", [7, 7]);
        var front = new TagPicture("FrontCover", "image/jpeg", "", [5, 5]);
        var tags = Tags("Quiet River", "Some Singer", pictures: [logo, back, front]);
        var local = Local(tags) with
        {
            Suggestions = [new CleanupSuggestion(CleanupField.Cover, null, "", "", CleanupReason.RepeatedCover, false) { CoverHash = CleanupAnalyzer.HashCover(logo.Data) }],
        };

        var merged = OnlineSuggestions.Merge(local, tags, Matched(MatchLevel.Strong), OnlineData.Release(), new CoverImage(OnlineData.Jpeg, "image/jpeg"), false);
        var cover = Of(merged, CleanupField.Cover);
        var change = CleanupAnalyzer.ToChange(tags, [cover], CleanupAnalyzer.HashCover);

        Assert.True(cover.Selected);
        Assert.Equal(["FrontCover", "BackCover"], change.Pictures!.Select(p => p.Type));
        Assert.Equal(OnlineData.Jpeg, change.Pictures![0].Data);
    }

    [Fact]
    public void A_new_cover_replaces_the_only_picture_whatever_it_is_called()
    {
        var tags = Tags("a", "b", pictures: [new TagPicture("Other", "image/jpeg", "", [1])]);
        var added = new TagPicture("FrontCover", "image/jpeg", "", OnlineData.Jpeg);

        var change = CleanupAnalyzer.ToChange(tags, [new CleanupSuggestion(CleanupField.Cover, null, "", "", CleanupReason.Online, true) { NewCover = added }],
            CleanupAnalyzer.HashCover);

        Assert.Equal([added], change.Pictures!);
    }

    [Fact]
    public void Years_and_numbers_are_written_as_numbers_in_any_digits()
    {
        var tags = Tags("a", "b");
        CleanupSuggestion Typed(CleanupField field, string value) => new(field, null, "", value, CleanupReason.Online, true);

        var change = CleanupAnalyzer.ToChange(tags, [Typed(CleanupField.Year, "۱۳۵۴"), Typed(CleanupField.Track, "next")], CleanupAnalyzer.HashCover);

        Assert.Equal(1354u, change.Year);
        Assert.Null(change.Track);
    }
}
