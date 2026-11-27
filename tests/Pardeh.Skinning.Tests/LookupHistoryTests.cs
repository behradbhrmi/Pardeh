using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;
using Pardeh.UI.OnlineViews;

namespace Pardeh.Skinning.Tests;

public sealed class LookupHistoryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-history-").FullName;
    private DateTime _now = new(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
    private bool _keep = true;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string FilePath => Path.Combine(_folder, "lookup-history.json");

    private LookupHistory History() => new(FilePath, () => _keep, () => _now);

    private static OnlineEntry Entry(OnlineGroup group = OnlineGroup.Strong, params MetadataCandidate[] found) => new()
    {
        LookedUp = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), Group = group, Song = OnlineData.Song(),
        Searched = new MetadataSearch("Quiet River", "Some Singer"),
        Candidates = found.Length > 0 || group == OnlineGroup.NoMatch ? found : [OnlineData.Candidate()],
    };

    [Fact]
    public void A_look_up_is_written_down_and_survives_a_restart_newest_first()
    {
        var history = History();
        history.AddSong("MusicBrainz", "/music/one.mp3", Entry());
        history.AddSearch("MusicBrainz", "Another Song", null, []);
        history.Flush();

        var again = History().Rows;

        Assert.Equal(["Another Song", "Quiet River"], again.Select(r => r.SearchedTitle));
        Assert.Equal(HistoryOutcome.NoMatch, again[0].Outcome);
        Assert.Null(again[0].Source);
        Assert.Equal(_now, again[0].When);
        var song = again[1];
        Assert.Equal("one.mp3", song.FileName);
        Assert.Equal("Some Singer", song.Artist);
        Assert.Equal(HistoryOutcome.Strong, song.Outcome);
        Assert.Equal("Morning Album", Assert.Single(song.Matches).Album);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), song.When);
    }

    [Fact]
    public void With_the_switch_off_nothing_is_written_down()
    {
        _keep = false;
        var history = History();

        history.AddSong("MusicBrainz", "/music/one.mp3", Entry());
        history.AddSearch("MusicBrainz", "Another Song", null, []);
        history.Flush();

        Assert.Empty(history.Rows);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Only_five_matches_and_the_newest_5000_rows_are_kept()
    {
        var history = new LookupHistory(null, now: () => _now);
        history.AddSong("MusicBrainz", "/m.mp3", Entry(OnlineGroup.Review, [.. Enumerable.Range(0, 8).Select(_ => OnlineData.Candidate())]));
        Assert.Equal(LookupHistory.KeptMatches, history.Rows[0].Matches.Count);

        for (var i = 0; i < LookupHistory.MaxRows; i++)
            history.AddSearch("MusicBrainz", "song " + i, null, []);

        Assert.Equal(LookupHistory.MaxRows, history.Count);
        Assert.Equal("song " + (LookupHistory.MaxRows - 1), history.Rows[0].SearchedTitle);
        Assert.Equal("song 0", history.Rows[^1].SearchedTitle);
    }

    [Fact]
    public void Saved_results_are_taken_in_once_and_not_again_after_clearing()
    {
        var saved = new Dictionary<string, OnlineEntry> { ["/music/one.mp3"] = Entry(), ["/music/two.mp3"] = Entry(OnlineGroup.NoMatch) };
        var history = History();

        history.Seed("MusicBrainz", saved);
        history.Seed("MusicBrainz", saved);
        Assert.Equal(2, history.Count);

        history.Clear();
        History().Seed("MusicBrainz", saved);
        Assert.Empty(History().Rows);
    }

    [Fact]
    public void Nothing_is_taken_in_while_no_history_is_kept_and_it_can_be_later()
    {
        var saved = new Dictionary<string, OnlineEntry> { ["/music/one.mp3"] = Entry() };
        _keep = false;
        History().Seed("MusicBrainz", saved);
        _keep = true;

        var history = History();
        history.Seed("MusicBrainz", saved);

        Assert.Single(history.Rows);
    }

    [Fact]
    public void A_renamed_file_keeps_its_rows_and_a_row_can_be_deleted()
    {
        var history = History();
        history.AddSong("MusicBrainz", "/music/one.mp3", Entry());

        history.Moved("/music/one.mp3", "/music/new name.mp3");
        Assert.Equal("/music/new name.mp3", history.Rows[0].Source);

        history.Delete(history.Rows[0].Id);
        Assert.Empty(History().Rows);
    }

    [Fact]
    public void The_search_looks_at_every_word_and_the_filters_narrow()
    {
        var history = new LookupHistory();
        history.AddSong("MusicBrainz", "/music/one.mp3", Entry());
        history.AddSearch("Wikimedia Commons", "Für Elise", null, []);
        history.AddSearch("MusicBrainz", "کوچه", "خواننده", []);

        List<string> Found(string? text, HistoryOutcome? outcome = null, string? service = null) =>
            [.. LookupHistory.Filter(history.Rows, text, outcome, service).Select(r => r.SearchedTitle)];

        Assert.Equal(["Quiet River"], Found("morning singer")); // an album and an artist of what was found
        Assert.Equal(["Für Elise"], Found("fur elise"));
        Assert.Equal(["کوچه"], Found("خواننده"));
        Assert.Equal(["Für Elise"], Found("", service: "Wikimedia Commons"));
        Assert.Equal(["Quiet River"], Found(null, HistoryOutcome.Strong));
        Assert.Empty(Found("nothing like it"));
    }

    [Fact]
    public void A_damaged_file_starts_an_empty_history()
    {
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(History().Rows);
    }

    [Fact]
    public void Only_a_real_id_makes_a_page_to_open()
    {
        var match = HistoryMatch.From(OnlineData.Candidate());

        Assert.Equal("https://musicbrainz.org/recording/" + OnlineData.RecordingId, LookupHistory.PageOf("MusicBrainz", match)!.AbsoluteUri);
        Assert.Null(LookupHistory.PageOf("MusicBrainz", match with { Id = "../../elsewhere" }));
        Assert.Null(LookupHistory.PageOf("Somewhere", match));
    }
}

public sealed class HistoryViewTests
{
    public HistoryViewTests() => TestPlatform.Ensure();

    private static void Click(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static LookupHistory Two()
    {
        var history = new LookupHistory();
        history.AddSearch("MusicBrainz", "Quiet River", "Some Singer", [HistoryMatch.From(OnlineData.Candidate())]);
        history.AddSearch("MusicBrainz", "Other Song", null, []);
        return history;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_list_follows_what_is_typed(bool narrow)
    {
        var view = new HistoryView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, new HistoryContext(Two()), () => { });
        Assert.Equal(2, view.Shown.Count);

        view.SearchBox.Text = "river";

        Assert.Equal("Quiet River", Assert.Single(view.Shown).SearchedTitle);
        Assert.Equal("1 of 2 look-ups.", view.Status);
    }

    [Fact]
    public void An_empty_history_says_that_opening_sends_nothing()
    {
        var view = new HistoryView(CleanupInk.Desktop, new HistoryContext(new LookupHistory()), () => { });

        Assert.Contains("sends nothing", view.Status);
    }

    [Fact]
    public void An_entry_offers_the_page_and_delete_and_fix_info_only_for_a_file_that_is_there()
    {
        var history = new LookupHistory();
        history.AddSearch("MusicBrainz", "Quiet River", "Some Singer", [HistoryMatch.From(OnlineData.Candidate())], "/music/one.mp3");
        var opened = new List<Uri>();
        var closed = 0;
        var gone = true;
        var context = new HistoryContext(history)
        {
            CanFixInfo = _ => true,
            FileState = _ => gone ? "This file is gone." : null,
            OpenPage = (page, _) =>
            {
                opened.Add(page);
                return Task.CompletedTask;
            },
        };

        var without = new HistoryEntryView(CleanupInk.Desktop, context, history.Rows[0], () => closed++);
        Assert.Contains("This file is gone.", Texts(without));
        Assert.DoesNotContain(without.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == "Fix Info...");
        gone = false;
        var view = new HistoryEntryView(CleanupInk.Desktop, context, history.Rows[0], () => closed++);
        Assert.Contains(view.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == "Fix Info...");

        Click(view, "Open on MusicBrainz");
        Click(view, "Delete");

        Assert.Equal("musicbrainz.org", Assert.Single(opened).Host);
        Assert.Empty(history.Rows);
        Assert.Equal(1, closed);
    }
}
