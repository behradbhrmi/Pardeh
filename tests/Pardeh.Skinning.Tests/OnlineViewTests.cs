using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;

namespace Pardeh.Skinning.Tests;

/// <summary>The online part of Fix Info, against a made-up database whose answers come when the test says.</summary>
public sealed class OnlineViewTests
{
    public OnlineViewTests() => TestPlatform.Ensure();

    private sealed class Database : IMetadataProvider
    {
        public int Searches, Covers;

        public IReadOnlyList<MetadataCandidate> Found { get; set; } = [OnlineData.Candidate()];

        public Exception? Failure { get; set; }

        /// <summary>Set to hold answers back until completed.</summary>
        public TaskCompletionSource? SearchGate { get; set; }

        public TaskCompletionSource? CoverGate { get; set; }

        public CancellationToken LastSearch, LastCover;

        public string Name => "Test";

        public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel)
        {
            Searches++;
            LastSearch = cancel;
            if (SearchGate is { } gate)
                await gate.Task;
            cancel.ThrowIfCancellationRequested();
            return Failure is null ? Found : throw Failure;
        }

        public async Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel, bool small = false)
        {
            Covers++;
            LastCover = cancel;
            if (CoverGate is { } gate)
                await gate.Task;
            return new CoverImage(OnlineData.Jpeg, "image/jpeg");
        }
    }

    private static readonly TagFields Current = new()
    {
        Title = "Quiet River | www.best-music.ir", Artists = ["Some Singer"], AlbumArtists = [], Album = "", Genres = [], Composers = [], Comment = "", Copyright = "",
        Year = 0, Track = 0, Texts = [], Pictures = [],
    };

    private readonly Database _database = new();
    private bool _allow = true;
    private int _asked;
    private ReviewResult? _result;

    private ReviewView Review(bool narrow = false)
    {
        var local = CleanupAnalyzer.Analyze(new TrackFacts("/m/x.mp3", "x.mp3", Current, null));
        var steps = MetadataLookup.Steps(Current, local, 200);
        var context = new OnlineContext(new MetadataLookup(_database), steps, steps[0].Song, () =>
        {
            _asked++;
            return Task.FromResult(_allow);
        }, () => false);
        return new ReviewView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, local, Current, new CleanupStore(), () => Task.CompletedTask, r => _result = r, context);
    }

    private static void Click(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    private Dictionary<CleanupField, CleanupSuggestion> Apply(ReviewView view)
    {
        Click(view, "Apply selected");
        return _result!.Choice!.Accepted.ToDictionary(s => s.Field);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nothing_is_sent_until_asked_for_and_agreed_to(bool narrow)
    {
        _allow = false;
        var view = Review(narrow);
        Assert.Equal(0, _database.Searches);
        Assert.Equal(0, _asked);

        Click(view, "Look up online");
        Click(view, "Search by hand...");

        Assert.Equal(2, _asked);
        Assert.Equal(0, _database.Searches);
        Assert.Equal(0, _database.Covers);
    }

    private ReviewView ReviewSaved(OnlineEntry saved, List<LookupResult>? remembered = null)
    {
        var local = CleanupAnalyzer.Analyze(new TrackFacts("/m/x.mp3", "x.mp3", Current, null));
        var steps = MetadataLookup.Steps(Current, local, 200);
        var context = new OnlineContext(new MetadataLookup(_database), steps, steps[0].Song, () => Task.FromResult(_allow), () => false)
        {
            Saved = saved,
            Remember = result => remembered?.Add(result),
        };
        return new ReviewView(CleanupInk.Desktop, local, Current, new CleanupStore(), () => Task.CompletedTask, r => _result = r, context);
    }

    private static OnlineEntry Saved(params MetadataCandidate[] found) => new()
    {
        LookedUp = new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc), Song = OnlineData.Song(), Candidates = found,
        Group = found.Length > 0 ? OnlineGroup.Strong : OnlineGroup.NoMatch,
    };

    [Fact]
    public void A_saved_result_is_shown_without_searching_again_and_what_is_taken_from_it_is_reported()
    {
        var view = ReviewSaved(Saved(OnlineData.Candidate()));

        Assert.Equal(0, _database.Searches);
        Assert.Contains("Looked up on", Texts(view));
        Assert.Equal("Morning Album", Apply(view)[CleanupField.Album].Suggested);
        Assert.Equal(OnlineData.RecordingId, _result!.Used!.Candidate.Id);
    }

    [Fact]
    public void A_saved_no_match_offers_another_look_which_is_remembered()
    {
        var remembered = new List<LookupResult>();
        var view = ReviewSaved(Saved(), remembered);
        Assert.Contains("No match on MusicBrainz.", Texts(view));
        Assert.Equal(0, _database.Searches);

        Click(view, "Look up again");

        Assert.Equal(1, _database.Searches);
        Assert.NotEmpty(Assert.Single(remembered).Matches);
    }

    [Fact]
    public void Applying_only_this_devices_fixes_reports_no_match_used()
    {
        var view = Review();

        Click(view, "Apply selected");

        Assert.Null(_result!.Used);
    }

    [Fact]
    public void A_strong_match_fills_the_fields_and_the_cover()
    {
        var view = Review();

        Click(view, "Look up online");

        Assert.Contains("Match: Strong", Texts(view));
        var accepted = Apply(view);
        Assert.Equal("Quiet River", accepted[CleanupField.Title].Suggested);
        Assert.Equal("Morning Album", accepted[CleanupField.Album].Suggested);
        Assert.Equal("1999", accepted[CleanupField.Year].Suggested);
        Assert.Equal("3", accepted[CleanupField.Track].Suggested);
        Assert.Equal(OnlineData.Jpeg, accepted[CleanupField.Cover].NewCover!.Data);
    }

    [Fact]
    public void A_cover_that_comes_late_leaves_what_was_typed()
    {
        // A delayed answer then carries on inside SetResult, on this thread.
        SynchronizationContext.SetSynchronizationContext(null);
        _database.CoverGate = new TaskCompletionSource();
        var view = Review();
        Click(view, "Look up online");
        var album = view.GetLogicalDescendants().OfType<TextBox>().Single(b => b.Text == "Morning Album");
        album.Text = "My own words";

        _database.CoverGate.SetResult();

        var accepted = Apply(view);
        Assert.Equal("My own words", accepted[CleanupField.Album].Suggested);
        Assert.True(accepted.ContainsKey(CleanupField.Cover));
    }

    [Fact]
    public void Without_a_match_the_local_fixes_stay()
    {
        _database.Found = [];
        var view = Review();

        Click(view, "Look up online");

        Assert.Contains("No match on MusicBrainz.", Texts(view));
        Assert.Equal("Quiet River", Assert.Single(Apply(view)).Value.Suggested);
    }

    [Fact]
    public void A_failure_is_shown_and_can_be_tried_again()
    {
        _database.Failure = new MetadataException("MusicBrainz is busy. Try again in a moment.");
        var view = Review();
        Click(view, "Look up online");
        Assert.Contains("is busy", Texts(view));

        _database.Failure = null;
        Click(view, "Try again");

        Assert.Contains("Match: Strong", Texts(view));
    }

    [Fact]
    public void Stopping_ignores_the_answer()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _database.SearchGate = new TaskCompletionSource();
        var view = Review();
        Click(view, "Look up online");

        Click(view, "Stop");
        Assert.True(_database.LastSearch.IsCancellationRequested);
        _database.SearchGate.SetResult();

        Assert.DoesNotContain("Match:", Texts(view));
        Assert.Equal(0, _database.Covers);
    }

    [Fact]
    public void A_year_that_is_no_number_stops_apply()
    {
        var view = Review();
        Click(view, "Look up online");
        view.GetLogicalDescendants().OfType<TextBox>().Single(b => b.Text == "1999").Text = "ninety";

        Click(view, "Apply selected");

        Assert.Null(_result);
        Assert.Contains("Year must be a number", Texts(view));
    }
}
