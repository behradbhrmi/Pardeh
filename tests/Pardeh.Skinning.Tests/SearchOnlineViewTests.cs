using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;
using Pardeh.UI.OnlineViews;

namespace Pardeh.Skinning.Tests;

public sealed class SearchOnlineViewTests
{
    public SearchOnlineViewTests() => TestPlatform.Ensure();

    private sealed class Database : IMetadataProvider
    {
        public readonly List<MetadataSearch> Asked = [];

        public IReadOnlyList<MetadataCandidate> Found { get; set; } = [];

        public string Name => "MusicBrainz";

        public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(MetadataSearch search, CancellationToken cancel)
        {
            Asked.Add(search);
            return Task.FromResult(Found);
        }

        public Task<CoverImage?> CoverAsync(MetadataRelease release, CancellationToken cancel, bool small = false) =>
            throw new InvalidOperationException("A search fetches no cover.");
    }

    private static MetadataCandidate Entry(string id, int releases, string title = "Quiet River") =>
        OnlineData.Candidate(title: title, releases: [.. Enumerable.Range(0, releases).Select(i => OnlineData.Release("Album " + i))]) with { Id = id };

    private static void Click(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    private const string Few = "44444444-4444-4444-4444-444444444444", Many = "55555555-5555-5555-5555-555555555555";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_the_typed_words_are_sent_once_agreed_and_the_result_is_remembered(bool narrow)
    {
        var database = new Database { Found = [Entry(Few, 1)] };
        var allow = false;
        var remembered = new List<LookupResult>();
        var view = new SearchOnlineView(narrow ? CleanupInk.Phone : CleanupInk.Desktop, new SearchOnlineContext
        {
            Lookup = new MetadataLookup(database), Allow = () => Task.FromResult(allow), Remember = remembered.Add,
        }, () => { });

        await view.Search();
        Assert.Contains("Type a song title first", Texts(view));
        view.TitleBox.Text = "  Quiet River ";
        view.ArtistBox.Text = "Some Singer";
        await view.Search();
        Assert.Empty(database.Asked);

        allow = true;
        await view.Search();

        Assert.Equal(new MetadataSearch("Quiet River", "Some Singer"), Assert.Single(database.Asked));
        Assert.Single(remembered);
        Assert.Contains("Found (1):", Texts(view));
    }

    [Fact]
    public async Task An_entry_with_few_releases_offers_the_most_released_one_of_the_same_song()
    {
        var database = new Database { Found = [Entry(Few, 1), Entry(Many, 4), Entry("66666666-6666-6666-6666-666666666666", 9, "Another Song")] };
        var asked = new List<(string, string?)>();
        var view = new SearchOnlineView(CleanupInk.Desktop, new SearchOnlineContext
        {
            Lookup = new MetadataLookup(database), Allow = () => Task.FromResult(true),
            WhereToGet = (target, other) =>
            {
                asked.Add((target.RecordingId, other?.RecordingId));
                return Task.CompletedTask;
            },
        }, () => { });
        view.TitleBox.Text = "Quiet River";
        await view.Search();

        foreach (var button in view.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Where to get it...").ToList())
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Contains((Few, Many), asked);
        Assert.Contains((Many, (string?)null), asked);
    }

    [Fact]
    public async Task A_repeated_search_is_remembered_until_a_fresh_answer_is_wanted()
    {
        var database = new Database { Found = [Entry(Few, 1)] };
        var lookup = new MetadataLookup(database);
        var song = OnlineData.Song();

        await lookup.SearchAsync("Quiet River", "", song, TestContext.Current.CancellationToken);
        await lookup.SearchAsync("Quiet River", "", song, TestContext.Current.CancellationToken);
        Assert.Single(database.Asked);

        lookup.Forget();
        await lookup.SearchAsync("Quiet River", "", song, TestContext.Current.CancellationToken);

        Assert.Equal(2, database.Asked.Count);
    }

    [Fact]
    public async Task Nothing_found_says_so()
    {
        var view = new SearchOnlineView(CleanupInk.Desktop, new SearchOnlineContext
        {
            Lookup = new MetadataLookup(new Database()), Allow = () => Task.FromResult(true),
        }, () => { });
        view.TitleBox.Text = "Nothing";

        await view.Search();

        Assert.Contains("found nothing", Texts(view));
    }
}
