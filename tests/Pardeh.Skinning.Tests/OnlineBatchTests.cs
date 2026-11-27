using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;

namespace Pardeh.Skinning.Tests;

/// <summary>Cases where a match that nobody looks at must not be taken for sure.</summary>
public sealed class MatchTuningTests
{
    private static MetadataCandidate By(string title, string artistText, params string[] artists) =>
        OnlineData.Candidate(title) with { ArtistText = artistText, Artists = [.. artists.Select(a => new ArtistCredit(a, []))] };

    private static MatchLevel Level(LocalSong song, MetadataCandidate candidate) => MetadataMatcher.Match(song, candidate).Level;

    [Theory]
    [InlineData("Big Artist feat. Guest", "Guest", "Guest")] // the guest's own recording
    [InlineData("Big Artist ft. Guest", "Guest", "Guest")]
    [InlineData("Guest", "Big Artist feat. Guest", "Big Artist,Guest")] // someone else's recording the artist only appears on
    [InlineData("Some Singer", "[unknown]", "[unknown]")]
    public void A_guest_or_nobody_is_not_sure(string songArtist, string credit, string artists)
    {
        var song = OnlineData.Song(artist: songArtist == "Some Singer" ? "[unknown]" : songArtist);

        Assert.NotEqual(MatchLevel.Strong, Level(song, By("Quiet River", credit, artists.Split(','))));
    }

    [Theory]
    [InlineData("Guest & Some Singer", "Some Singer & Guest", "Some Singer,Guest")]
    [InlineData("Some Singer", "Some Singer feat. Guest", "Some Singer,Guest")]
    [InlineData("Simon and Garfunkel", "Simon & Garfunkel", "Simon & Garfunkel")]
    [InlineData("Beyonce", "Beyoncé", "Beyoncé")]
    public void The_same_artists_written_another_way_are_sure(string songArtist, string credit, string artists) =>
        Assert.Equal(MatchLevel.Strong, Level(OnlineData.Song(artist: songArtist), By("Quiet River", credit, artists.Split(','))));

    [Theory]
    [InlineData("اهنگ باران", "آهنگ باران")]
    [InlineData("خاطره", "خاطرۀ")]
    [InlineData("رويا", "رؤیا")] // with and without the hamza, and an Arabic keyboard's yeh
    [InlineData("باران", "یاران", false)] // other dots, another word
    [InlineData("مسئله", "مسیله")]
    [InlineData("می‌روم", "میروم")]
    [InlineData("كوچه علي", "کوچه علی")]
    public void Persian_spellings_that_differ_only_in_marks_are_one_name(string typed, string known, bool same = true) =>
        Assert.Equal(same, MetadataMatcher.SameName(typed, known));

    [Fact]
    public void Letters_that_marks_tell_apart_in_other_scripts_stay_apart() => Assert.False(MetadataMatcher.SameName("かき", "がき"));

    [Theory]
    [InlineData("Quiet River (Sped Up)")]
    [InlineData("Quiet River [Slowed + Reverb]")]
    [InlineData("Quiet River (Cover)")]
    [InlineData("Quiet River (Acapella)")]
    [InlineData("Quiet River (Extended)")]
    [InlineData("Quiet River - Nightcore")]
    [InlineData("Quiet River (Reprise)")]
    [InlineData("Quiet River (کنسرت)")]
    public void Another_version_is_not_sure(string title) =>
        Assert.NotEqual(MatchLevel.Strong, Level(OnlineData.Song(title), OnlineData.Candidate()));

    [Fact]
    public void A_database_entry_noted_as_a_cover_is_not_the_plain_song() =>
        Assert.NotEqual(MatchLevel.Strong, Level(OnlineData.Song(), OnlineData.Candidate(disambiguation: "tribute band cover")));

    [Fact]
    public void With_no_length_that_fits_the_well_known_recording_comes_before_a_bare_entry()
    {
        var bare = OnlineData.Candidate(seconds: 0, releases: [OnlineData.Release("Bootleg", group: "b")]);
        var known = OnlineData.Candidate(releases: [OnlineData.Release(group: "a"), OnlineData.Release("Second", group: "c")]) with { Id = "known" };

        Assert.Equal("known", MetadataMatcher.Rank(OnlineData.Song(seconds: 240), [bare, known])[0].Candidate.Id);
    }

    [Theory]
    [InlineData("Intro")]
    [InlineData("Outro")]
    [InlineData("مقدمه")]
    public void A_title_every_album_has_needs_the_album_too(string title)
    {
        Assert.Equal(MatchLevel.Possible, Level(OnlineData.Song(title), OnlineData.Candidate(title)));
        Assert.Equal(MatchLevel.Strong, Level(OnlineData.Song(title, album: "Morning Album"), OnlineData.Candidate(title)));
    }
}

public sealed class OnlineJudgeTests
{
    internal static readonly TagFields Junked = new()
    {
        Title = "Quiet River | www.best-music.ir", Artists = ["Some Singer"], AlbumArtists = [], Album = "", Genres = [], Composers = [], Comment = "", Copyright = "",
        Year = 0, Track = 0, Texts = [], Pictures = [],
    };

    private static readonly TagFields Right = Junked with
    {
        Title = "Quiet River", Album = "Morning Album", AlbumArtists = ["Some Singer"], Year = 1999, Track = 3,
    };

    private static OnlineVerdict Judge(TagFields tags, string file = "x.mp3", params MetadataCandidate[] found)
    {
        var local = CleanupAnalyzer.Analyze(new TrackFacts("/m/" + file, file, tags, null));
        var steps = MetadataLookup.Steps(tags, local, 200);
        var candidates = found.Length > 0 ? found : [OnlineData.Candidate()];
        // As the look-up does: the first reading that matches.
        var step = steps.FirstOrDefault(s => MetadataMatcher.Rank(s.Song, candidates).Any(m => m.Level != MatchLevel.None)) ?? steps[0];
        return OnlineJudge.Judge(local, tags, step.Song, [.. MetadataMatcher.Rank(step.Song, candidates).Where(m => m.Level != MatchLevel.None)], false);
    }

    private static IEnumerable<CleanupField> Ticked(OnlineVerdict verdict) => verdict.Proposal.Suggestions.Where(s => s.Selected).Select(s => s.Field);

    [Fact]
    public void A_strong_match_that_fills_something_is_ticked()
    {
        var verdict = Judge(Junked);

        Assert.Equal(OnlineGroup.Strong, verdict.Group);
        Assert.True(verdict.WantsCover);
        Assert.Superset(new HashSet<CleanupField> { CleanupField.Title, CleanupField.Album, CleanupField.Year, CleanupField.Track }, Ticked(verdict).ToHashSet());
    }

    [Fact]
    public void A_song_that_is_right_already_is_checked() =>
        Assert.Equal((OnlineGroup.Checked, false),
            (Judge(Right with { Pictures = [new TagPicture("FrontCover", "image/jpeg", "", OnlineData.Jpeg)] }).Group, Judge(Right with { Pictures = [new TagPicture("FrontCover", "image/jpeg", "", OnlineData.Jpeg)] }).WantsCover));

    [Fact]
    public void Nothing_found_is_no_match_and_a_possible_one_needs_a_look()
    {
        Assert.Equal(OnlineGroup.NoMatch, Judge(Junked, found: OnlineData.Candidate("Loud Mountain", "Nobody", 90)).Group);
        Assert.Equal(OnlineGroup.Review, Judge(Junked, found: OnlineData.Candidate(seconds: 230)).Group);
    }

    [Fact]
    public void A_match_the_files_own_details_contradict_needs_a_look_and_fills_nothing()
    {
        // The tags name another song; only the file name leads to the match.
        var verdict = Judge(Junked with { Title = "Another Song Entirely", Artists = ["Else"] }, "Some Singer - Quiet River.mp3");

        Assert.Equal(OnlineGroup.Review, verdict.Group);
        Assert.Equal(MatchLevel.Strong, verdict.Match!.Level);
        Assert.DoesNotContain(verdict.Proposal.Suggestions, s => s is { Selected: true, Reason: CleanupReason.Online });
    }

    [Fact]
    public void Title_and_artist_the_wrong_way_round_need_a_look() =>
        Assert.Equal(OnlineGroup.Review, Judge(Junked with { Title = "Some Singer", Artists = ["Quiet River"] }).Group);

    [Theory]
    [InlineData("Compilation", true)]
    [InlineData("Live", false)]
    [InlineData("Soundtrack", true)]
    public void The_album_is_not_ticked_from_a_release_that_may_not_be_the_files(string kind, bool official)
    {
        var verdict = Judge(Junked, found: OnlineData.Candidate(releases: [OnlineData.Release("Big Hits", secondary: [kind], official: official)]));

        Assert.DoesNotContain(CleanupField.Album, Ticked(verdict));
        Assert.DoesNotContain(CleanupField.Track, Ticked(verdict));
        // Still there to be chosen.
        Assert.Contains(verdict.Proposal.Suggestions, s => s is { Field: CleanupField.Album, Suggested: "Big Hits" });
    }

    [Fact]
    public void The_year_is_not_ticked_when_another_sure_entry_is_earlier()
    {
        var later = OnlineData.Candidate(first: "2011", releases: [OnlineData.Release(date: "2011"), OnlineData.Release("Other", "2012", group: "o")]);
        var earlier = OnlineData.Candidate(first: "1999") with { Id = "44444444-4444-4444-4444-444444444444" };

        var verdict = Judge(Junked, found: [later, earlier]);

        Assert.Equal(2011u, verdict.Match!.Candidate.FirstYear);
        Assert.DoesNotContain(CleanupField.Year, Ticked(verdict));
    }
}

public sealed class OnlineStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-online-").FullName;
    private DateTime _now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Modified = new(2025, 5, 5, 5, 5, 5, 123, DateTimeKind.Utc);

    private OnlineStore Store() => new(Path.Combine(_folder, "online.json"), () => _now);

    private OnlineEntry Entry(OnlineGroup group = OnlineGroup.Strong, params MetadataCandidate[] found) => new()
    {
        Size = 100, Modified = Modified, LookedUp = _now, Matcher = OnlineStore.MatcherVersion, Group = group, Song = OnlineData.Song("رود آرام"),
        Searched = new MetadataSearch("رود آرام", "Some Singer"), Candidates = found.Length > 0 ? found : [OnlineData.Candidate("رود آرام")],
        Fields = [CleanupField.Album],
    };

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void A_result_is_there_after_a_restart_and_judges_the_same()
    {
        var store = Store();
        store.Put("/m/a.mp3", Entry());
        store.Flush();

        var again = Store().Get("/m/a.mp3", 100, Modified)!;

        Assert.Equal((OnlineGroup.Strong, "رود آرام", CleanupField.Album), (again.Group, again.Song.Title, Assert.Single(again.Fields)));
        Assert.Equal(MatchLevel.Strong, again.Matches()[0].Level);
        Assert.Equal("Morning Album", again.Candidates[0].Releases[0].Title);
        Assert.Equal(3u, again.Candidates[0].Releases[0].Track);
    }

    [Theory]
    [InlineData(101, 0)]
    [InlineData(100, 2)]
    public void A_changed_file_has_no_result_and_is_due(long size, int seconds)
    {
        var store = Store();
        store.Put("/m/a.mp3", Entry());

        Assert.Null(store.Get("/m/a.mp3", size, Modified.AddSeconds(seconds)));
        Assert.True(store.IsDue("/m/a.mp3", size, Modified.AddSeconds(seconds)));
        Assert.False(store.IsDue("/m/a.mp3", 100, Modified));
    }

    [Fact]
    public void Forgetting_a_file_drops_its_result_for_good()
    {
        var store = Store();
        store.Put("/m/a.mp3", Entry());

        store.Forget(["/m/a.mp3"]);

        Assert.Null(Store().Get("/m/a.mp3", 100, Modified));
    }

    [Fact]
    public void No_match_is_tried_again_only_after_a_while_and_other_results_stay()
    {
        var store = Store();
        store.Put("/m/none.mp3", Entry(OnlineGroup.NoMatch));
        store.Put("/m/found.mp3", Entry());

        _now += OnlineStore.RetryNoMatchAfter - TimeSpan.FromMinutes(1);
        Assert.False(store.IsDue("/m/none.mp3", 100, Modified));
        _now += TimeSpan.FromMinutes(2);

        Assert.True(store.IsDue("/m/none.mp3", 100, Modified));
        Assert.False(store.IsDue("/m/found.mp3", 100, Modified));
    }

    [Fact]
    public void Only_the_best_few_recordings_and_releases_are_kept()
    {
        var releases = Enumerable.Range(0, 40).Select(i => OnlineData.Release($"Release {i}", (1990 + i).ToString(), group: $"g{i}")).ToArray();
        var store = Store();

        store.Put("/m/a.mp3", Entry(found: [.. Enumerable.Range(0, 30).Select(_ => OnlineData.Candidate("رود آرام", releases: releases))]));

        var kept = store.Get("/m/a.mp3", 100, Modified)!;
        Assert.Equal(5, kept.Candidates.Count);
        Assert.All(kept.Candidates, c => Assert.Equal(6, c.Releases.Count));
    }

    [Fact]
    public void A_damaged_file_starts_empty()
    {
        File.WriteAllText(Path.Combine(_folder, "online.json"), "{\"Version\":1,\"Songs\":{\"a\":nul");

        Assert.True(Store().IsDue("a", 100, Modified));
    }

    [Theory]
    [InlineData(false, OnlineGroup.Strong, false, FixInfoState.MatchFound)]
    [InlineData(true, OnlineGroup.Strong, false, FixInfoState.Ignored)]
    [InlineData(false, OnlineGroup.Checked, false, FixInfoState.Checked)]
    [InlineData(false, OnlineGroup.Checked, true, FixInfoState.Checked)]
    [InlineData(false, OnlineGroup.NoMatch, false, FixInfoState.NoMatch)]
    [InlineData(false, OnlineGroup.Review, false, FixInfoState.None)]
    public void The_state_of_a_song_follows_what_is_saved(bool ignored, OnlineGroup group, bool applied, FixInfoState state) =>
        Assert.Equal(state, FixInfoStates.Of(ignored, null, Entry(group) with { Applied = applied }));

    [Fact]
    public void A_local_problem_shows_over_a_check_that_found_nothing_to_change()
    {
        var local = CleanupAnalyzer.Analyze(new TrackFacts("/m/x.mp3", "x.mp3", OnlineJudgeTests.Junked, null));

        Assert.Equal(FixInfoState.NeedsCleanup, FixInfoStates.Of(false, local, Entry(OnlineGroup.NoMatch)));
        Assert.Equal(FixInfoState.NeedsCleanup, FixInfoStates.Of(false, local, null));
        Assert.Equal(FixInfoState.MatchFound, FixInfoStates.Of(false, local, Entry()));
    }
}

/// <summary>A made-up library check: songs by source, a database that answers or fails as told, and no real waiting.</summary>
internal sealed class CheckRig : IMetadataProvider
{
    public static readonly DateTime Modified = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public readonly OnlineStore Store;
    public readonly SongChecker Checker;
    public readonly List<TimeSpan> Waits = [];
    public readonly List<string> Searched = [];
    public readonly List<(string Release, bool Small)> CoverAsks = [];
    public Func<string, IReadOnlyList<MetadataCandidate>> Answer { get; set; } = title => [OnlineData.Candidate(title)];
    public Func<MetadataRelease, CoverImage?> Cover { get; set; } = _ => new CoverImage(OnlineData.Jpeg, "image/jpeg");
    public Dictionary<string, TagFields?> Tags { get; } = [];
    public bool Allowed = true;
    public LibraryCheck Check { get; }

    public CheckRig(DateTime? now = null)
    {
        Store = new OnlineStore(null, () => now ?? DateTime.UtcNow);
        Checker = new SongChecker(new MetadataLookup(this), Store, Read, () => false);
        Check = new LibraryCheck(Checker, () => Allowed, (wait, _) =>
        {
            Waits.Add(wait);
            return Task.CompletedTask;
        });
    }

    public string Name => "Test";

    public SongToCheck Song(string title, TagFields? tags = null)
    {
        var source = $"/m/{title}.mp3";
        Tags[source] = tags ?? OnlineJudgeTests.Junked with { Title = title };
        return new SongToCheck(source, 100, Modified, 200);
    }

    public Task<SongFacts?> Read(string source) => Task.FromResult(Tags.GetValueOrDefault(source) is { } tags
        ? new SongFacts(CleanupAnalyzer.Analyze(new TrackFacts(source, Path.GetFileName(source), tags, null)), tags, JunkRules.None)
        : null);

    public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        Searched.Add(search.Title);
        return Task.FromResult(Answer(search.Title));
    }

    public Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel, bool small = false)
    {
        CoverAsks.Add((release.Id, small));
        return Task.FromResult(Cover(release));
    }

    public OnlineEntry? Saved(SongToCheck song) => Store.Get(song.Source, song.Size, song.Modified);

    public async Task Run(params SongToCheck[] songs)
    {
        Check.Start(songs);
        await Check.Completion.WaitAsync(TimeSpan.FromSeconds(20));
    }
}

public sealed class LibraryCheckTests
{
    private static async Task Until(Func<bool> true_)
    {
        for (var i = 0; i < 1000 && !true_(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(true_());
    }

    [Fact]
    public async Task Every_song_is_looked_up_and_saved()
    {
        var rig = new CheckRig();
        var songs = new[] { rig.Song("Quiet River"), rig.Song("Loud Mountain") };

        await rig.Run(songs);

        Assert.Equal((CheckState.Done, 2, 2, 0), (rig.Check.Progress.State, rig.Check.Progress.Done, rig.Check.Progress.Total, rig.Check.Progress.Failed));
        Assert.All(songs, s => Assert.Equal(OnlineGroup.Strong, rig.Saved(s)!.Group));
    }

    [Fact]
    public async Task A_song_the_service_could_not_be_asked_about_is_not_a_no_match()
    {
        var rig = new CheckRig();
        var bad = rig.Song("Broken");
        var good = rig.Song("Quiet River");
        rig.Answer = title => title == "Broken" ? throw new MetadataException("MusicBrainz is busy.") : [OnlineData.Candidate(title)];

        await rig.Run(bad, good);

        Assert.Null(rig.Saved(bad));
        Assert.True(rig.Store.IsDue(bad.Source, bad.Size, bad.Modified));
        Assert.NotNull(rig.Saved(good));
        Assert.Equal((CheckState.Done, 2, 1), (rig.Check.Progress.State, rig.Check.Progress.Done, rig.Check.Progress.Failed));
        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)], rig.Waits);
    }

    [Fact]
    public async Task When_the_service_keeps_failing_it_pauses_and_resuming_goes_on_with_the_same_song()
    {
        var rig = new CheckRig();
        var songs = new[] { rig.Song("One Song"), rig.Song("Two Song"), rig.Song("Three Song") };
        var down = true;
        rig.Answer = title => down ? throw new MetadataException("MusicBrainz couldn't be reached.") : [OnlineData.Candidate(title)];

        rig.Check.Start(songs);
        await Until(() => rig.Check.Progress.State == CheckState.Paused);

        Assert.StartsWith("MusicBrainz couldn't be reached.", rig.Check.Progress.Message);
        Assert.Equal(1, rig.Check.Progress.Done); // the first was given up on after two tries
        Assert.Equal([10, 30, 60], rig.Waits.Select(w => (int)w.TotalSeconds));
        var asked = rig.Searched.Count;
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(asked, rig.Searched.Count);

        down = false;
        rig.Check.Resume();
        await rig.Check.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal((CheckState.Done, 3), (rig.Check.Progress.State, rig.Check.Progress.Done));
        Assert.Equal([false, true, true], songs.Select(s => rig.Saved(s) is not null));
    }

    [Fact]
    public async Task Pausing_asks_nothing_more_and_stopping_keeps_what_was_found()
    {
        var rig = new CheckRig();
        var songs = new[] { rig.Song("One Song"), rig.Song("Two Song") };
        rig.Answer = title =>
        {
            rig.Check.Pause(); // while the first is being asked about
            return [OnlineData.Candidate(title)];
        };

        rig.Check.Start(songs);
        await Until(() => rig.Check.Progress is { State: CheckState.Paused, Done: 1 });
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Single(rig.Searched);

        rig.Check.Stop();
        await rig.Check.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal((CheckState.Stopped, 1), (rig.Check.Progress.State, rig.Check.Progress.Done));
        Assert.NotNull(rig.Saved(songs[0]));
        Assert.Null(rig.Saved(songs[1]));
        Assert.False(rig.Check.Progress.IsActive);
    }

    [Fact]
    public async Task It_waits_while_files_are_being_changed()
    {
        var rig = new CheckRig();
        var hold = rig.Check.Hold();

        rig.Check.Start([rig.Song("Quiet River")]);
        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.Empty(rig.Searched);
        hold.Dispose();
        await rig.Check.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Single(rig.Searched);
    }

    [Fact]
    public async Task Turning_look_ups_off_ends_it_before_anything_more_is_sent()
    {
        var rig = new CheckRig { Allowed = false };

        await rig.Run(rig.Song("Quiet River"));

        Assert.Empty(rig.Searched);
        Assert.Equal(CheckState.Stopped, rig.Check.Progress.State);
        Assert.NotNull(rig.Check.Progress.Message);
    }

    [Fact]
    public async Task Songs_with_nothing_to_search_for_or_that_cant_be_read_are_passed_over()
    {
        var rig = new CheckRig();
        var untitled = rig.Song("123", OnlineJudgeTests.Junked with { Title = "", Artists = [] });
        var gone = new SongToCheck("/m/gone.mp3", 1, CheckRig.Modified, 10);

        await rig.Run(untitled, gone);

        Assert.Empty(rig.Searched);
        Assert.Equal((CheckState.Done, 2), (rig.Check.Progress.State, rig.Check.Progress.Skipped));
        Assert.Null(rig.Saved(untitled));
        Assert.True(rig.Check.WasPassedOver(untitled));
        Assert.False(rig.Check.WasPassedOver(untitled with { Size = 101 }));
    }

    // ---- the playing song ------------------------------------------------------------------------

    private static PlayingSongCheck Playing(CheckRig rig, Dictionary<string, SongToCheck> songs, Func<bool> enabled, Func<CancellationToken, Task>? settle = null) =>
        new(rig.Checker, enabled, s => songs.GetValueOrDefault(s) is { } song && rig.Store.IsDue(song.Source, song.Size, song.Modified) ? song : null,
            (_, cancel) => settle?.Invoke(cancel) ?? Task.CompletedTask);

    [Fact]
    public async Task The_playing_song_is_asked_about_once_and_then_remembered()
    {
        var rig = new CheckRig();
        var song = rig.Song("Quiet River");
        var check = Playing(rig, new() { [song.Source] = song }, () => true);

        check.Playing(song.Source);
        await check.Completion;
        check.Playing(song.Source);
        check.Playing(null);
        check.Playing(song.Source);
        await check.Completion;

        Assert.Single(rig.Searched);
        Assert.NotNull(rig.Saved(song));
    }

    [Fact]
    public async Task Nothing_is_sent_while_off_or_for_a_song_skipped_past()
    {
        var rig = new CheckRig();
        var first = rig.Song("One Song");
        var second = rig.Song("Two Song");
        var songs = new Dictionary<string, SongToCheck> { [first.Source] = first, [second.Source] = second };
        var on = false;
        var settled = new List<TaskCompletionSource>();
        var check = Playing(rig, songs, () => on, cancel =>
        {
            var gate = new TaskCompletionSource();
            cancel.Register(() => gate.TrySetCanceled(cancel));
            settled.Add(gate);
            return gate.Task;
        });

        check.Playing(first.Source);
        Assert.Empty(settled);
        on = true;
        check.Playing(null);
        check.Playing(first.Source);
        check.Playing(second.Source); // before the first has played long enough
        settled[^1].SetResult();
        await check.Completion;

        Assert.Equal(["Two Song"], rig.Searched);
    }

    [Fact]
    public async Task After_a_failure_the_playing_check_keeps_quiet_for_a_while()
    {
        var rig = new CheckRig();
        var first = rig.Song("One Song");
        var second = rig.Song("Two Song");
        rig.Answer = _ => throw new MetadataException("busy");
        var check = Playing(rig, new() { [first.Source] = first, [second.Source] = second }, () => true);

        check.Playing(first.Source);
        await check.Completion;
        check.Playing(second.Source);
        await check.Completion;

        Assert.Single(rig.Searched);
        Assert.Null(rig.Saved(first));
    }

    // ---- preparing a batch -----------------------------------------------------------------------

    private static async Task<PreparedBatch> Prepare(CheckRig rig, bool covers, params SongToCheck[] songs) =>
        await OnlineBatch.PrepareAsync([.. songs.Select(s => new BatchPick(s.Source, rig.Saved(s)))], rig.Read, rig, covers, null, false,
            cancel: TestContext.Current.CancellationToken);

    [Fact]
    public async Task Covers_are_downloaded_only_when_asked_for_small_and_once_a_release()
    {
        var rig = new CheckRig();
        var songs = new[] { rig.Song("One Song"), rig.Song("Two Song") };
        await rig.Run(songs);
        Assert.Empty(rig.CoverAsks);

        var without = await Prepare(rig, false, songs);
        Assert.Empty(rig.CoverAsks);
        Assert.Equal((2, 0), (without.Choices.Count, without.Covers));
        Assert.DoesNotContain(without.Choices.SelectMany(c => c.Accepted), s => s.NewCover is not null);

        var with = await Prepare(rig, true, songs);

        Assert.Equal([(OnlineData.ReleaseId, true)], rig.CoverAsks);
        Assert.Equal(2, with.Covers);
        Assert.All(with.Choices, c => Assert.Contains(c.Accepted, s => s.NewCover is not null));
        Assert.Equal(2, with.Used.Count);
    }

    [Fact]
    public async Task A_cover_that_cant_be_fetched_leaves_the_other_changes_and_repeated_failures_stop_asking()
    {
        var rig = new CheckRig();
        var songs = new[] { "Amber", "Birch", "Cedar", "Delta", "Ember" }.Select(t => rig.Song(t + " Tune")).ToArray();
        var n = 0;
        rig.Answer = title => [OnlineData.Candidate(title, releases: [OnlineData.Release(id: $"22222222-2222-2222-2222-22222222222{n++}")])];
        await rig.Run(songs);
        rig.Cover = _ => throw new MetadataException("busy");

        var batch = await Prepare(rig, true, songs);

        Assert.Equal((5, 0, 5), (batch.Choices.Count, batch.Covers, batch.CoversFailed));
        Assert.Equal(3, rig.CoverAsks.Count);
    }

    [Fact]
    public async Task A_song_that_changed_since_it_was_listed_is_left_out()
    {
        var rig = new CheckRig();
        var song = rig.Song("Quiet River");
        await rig.Run(song);
        var entry = rig.Saved(song);
        rig.Tags[song.Source] = OnlineJudgeTests.Junked with { Title = "Something Else", Artists = ["Somebody"] };

        var batch = await OnlineBatch.PrepareAsync([new BatchPick(song.Source, entry)], rig.Read, rig, true, null, false, cancel: TestContext.Current.CancellationToken);

        Assert.Equal((0, 1), (batch.Choices.Count, batch.NotSure));
        Assert.Empty(rig.CoverAsks);
    }

    [Fact]
    public async Task The_small_cover_is_the_only_size_asked_for_in_a_batch()
    {
        var web = new FakeWeb();
        var provider = new MusicBrainzProvider(new HttpClient(web), "Pardeh/9 (x)", throttle: new RequestThrottle(TimeSpan.Zero));

        Assert.Null(await provider.CoverAsync(OnlineData.Release(), TestContext.Current.CancellationToken, small: true));

        Assert.All(web.Asked, a => Assert.EndsWith("front-500", a.Address));
        Assert.NotEmpty(web.Asked);
    }

}

/// <summary>The "Check Library Online" screen over a made-up library, on both layouts.</summary>
public sealed class LibraryCheckViewTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-check-").FullName;
    private readonly CheckRig _rig = new();
    private readonly MediaLibrary _library;
    private readonly CleanupStore _cleanupStore = new();
    private readonly List<IReadOnlyList<SongToCheck>> _started = [];
    private (IReadOnlyList<BatchPick> Picks, bool Covers, string? Pattern)? _applied;

    public LibraryCheckViewTests()
    {
        TestPlatform.Ensure();
        _library = new MediaLibrary(new FakeInfoReader(), Path.Combine(_folder, "library.json"));
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private LibraryItem Add(string name, OnlineGroup? group = null, bool wantsCover = false)
    {
        var path = Path.Combine(_folder, name + ".mp3");
        File.WriteAllBytes(path, [1, 2, 3]);
        _library.AddFiles([path]);
        // The scan runs in the background.
        Assert.True(SpinWait.SpinUntil(() => _library.Find(path) is not null && !_library.IsScanning, TimeSpan.FromSeconds(10)));
        var item = _library.Find(path)!;
        if (group is { } found)
        {
            _rig.Store.Put(path, new OnlineEntry
            {
                Size = item.FileSize, Modified = item.Modified, LookedUp = _rig.Store.Now, Matcher = OnlineStore.MatcherVersion, Group = found,
                Song = OnlineData.Song(), Candidates = found == OnlineGroup.NoMatch ? [] : [OnlineData.Candidate()],
                Fields = found == OnlineGroup.Strong ? [CleanupField.Album, CleanupField.Year] : [], Why = "Length differs by 30 s", WantsCover = wantsCover,
            });
        }

        return item;
    }

    private LibraryCheckView Screen(bool narrow)
    {
        var scanner = new CleanupScanner(_library, new NoFacts(), _cleanupStore, null);
        var context = new LibraryCheckContext(_rig.Check, _rig.Store, _library, scanner)
        {
            Start = songs =>
            {
                _started.Add(songs);
                return Task.CompletedTask;
            },
            Apply = (picks, covers, pattern) => _applied = (picks, covers, pattern),
        };
        return new LibraryCheckView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, context, () => { });
    }

    private sealed class NoFacts : ITrackFactsReader
    {
        public TrackFacts? Read(string source) => null;
    }

    private static Button Button(Control root, string start) =>
        root.GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as string)?.StartsWith(start, StringComparison.Ordinal) == true);

    private static void Click(Control root, string start) => Button(root, start).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private static CheckBox Tick(Control root, string text) =>
        root.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Content is TextBlock { Text: var label } && label == text);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_strong_matches_are_ticked_and_covers_and_renaming_start_off(bool narrow)
    {
        var strong = Add("strong", OnlineGroup.Strong, wantsCover: true);
        Add("review", OnlineGroup.Review);
        Add("none", OnlineGroup.NoMatch);
        Add("right", OnlineGroup.Checked, wantsCover: true);
        var view = Screen(narrow);

        Click(view, "Apply to 1 song");

        var (picks, covers, pattern) = _applied!.Value;
        Assert.Equal(strong.Path, Assert.Single(picks).Source);
        Assert.Null(picks[0].Reviewed);
        Assert.False(covers);
        Assert.Null(pattern);
    }

    [Fact]
    public void Asking_for_covers_adds_the_songs_that_only_lack_one()
    {
        Add("strong", OnlineGroup.Strong);
        var right = Add("right", OnlineGroup.Checked, wantsCover: true);
        Add("complete", OnlineGroup.Checked);
        var view = Screen(false);

        Tick(view, "Also add missing covers").IsChecked = true;
        Click(view, "Apply to 2 songs");

        Assert.True(_applied!.Value.Covers);
        Assert.Contains(_applied.Value.Picks, p => p.Source == right.Path);
    }

    [Fact]
    public void Only_songs_due_are_offered_for_checking_and_ignored_ones_are_left_out()
    {
        Add("known", OnlineGroup.Strong);
        Add("unmatched", OnlineGroup.NoMatch); // asked about too recently
        var fresh = Add("fresh");
        _cleanupStore.SetIgnored(Add("ignored").Path, true);
        var view = Screen(false);

        Click(view, "Check 1 song...");

        Assert.Equal(fresh.Path, Assert.Single(Assert.Single(_started)).Source);
    }

    [Fact]
    public void With_nothing_ticked_there_is_nothing_to_apply()
    {
        Add("review", OnlineGroup.Review);
        var view = Screen(true);

        Assert.False(Button(view, "Apply").IsEnabled);
    }

    [Fact]
    public void A_progress_report_that_arrives_late_does_not_cover_the_list_again()
    {
        Add("strong", OnlineGroup.Strong);
        var view = Screen(true);
        var report = view.Busy("Changing files", null);
        report((1, 2));
        Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Changing files");
        Assert.False(Button(view, "Apply").IsEffectivelyVisible);

        view.ShowList(changed: true);
        report((2, 2));

        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Panel>().Where(p => p.IsVisible).SelectMany(p => p.Children).OfType<StackPanel>(),
            s => s.IsVisible && s.Children.OfType<TextBlock>().Any(t => t.Text == "Changing files"));
        Assert.True(Button(view, "Apply").IsVisible);
    }

    [Theory]
    [InlineData(10, "under a minute")]
    [InlineData(100, "about 2 minutes")]
    [InlineData(3600, "about 1 hour")]
    [InlineData(5000, "about 1 hour 24 minutes")]
    public void Times_are_said_roughly(int seconds, string said) => Assert.Equal(said, LibraryCheckView.About(TimeSpan.FromSeconds(seconds)));
}
