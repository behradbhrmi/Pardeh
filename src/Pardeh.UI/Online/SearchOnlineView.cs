using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Pardeh.Core.Cleanup;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.OnlineViews;

/// <summary>What the "Search Online" screen works with.</summary>
internal sealed record SearchOnlineContext
{
    public required MetadataLookup Lookup { get; init; }

    /// <summary>Whether the user has agreed to online look-ups; asks when not.</summary>
    public required Func<Task<bool>> Allow { get; init; }

    /// <summary>Told what a search found, for the history.</summary>
    public Action<LookupResult> Remember { get; init; } = _ => { };

    /// <summary>"Where to get it", with another entry to try if the first has no links.</summary>
    public Func<LinkTarget, LinkTarget?, Task> WhereToGet { get; init; } = (_, _) => Task.CompletedTask;

    public Func<Uri, string, Task> OpenPage { get; init; } = (_, _) => Task.CompletedTask;
}

/// <summary>
/// A search for any song by title and artist, in the library or not. Only the typed words are sent, and only when Search is
/// pressed.
/// </summary>
internal sealed class SearchOnlineView : UserControl
{
    private const int Listed = 25;

    private readonly CleanupInk _ink;
    private readonly SearchOnlineContext _context;
    private readonly TextBox _title;
    private readonly TextBox _artist;
    private readonly Button _go;
    private readonly StackPanel _results = new() { Spacing = 8 };
    private CancellationTokenSource? _search;

    /// <param name="more">Further ways to search, under the boxes.</param>
    public SearchOnlineView(CleanupInk ink, SearchOnlineContext context, Action close, Control? more = null)
    {
        _ink = ink;
        _context = context;
        _title = Box("Song title");
        _artist = Box("Artist (may be left empty)");
        _go = ink.Button("Search", () => _ = Search(), primary: true);
        _go.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var top = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                ink.Label("Any song, in your library or not. The words you type are sent to MusicBrainz, which has song details and links, no music.",
                    12, colour: ink.Sub),
                _title, _artist, _go,
            },
        };

        var closeButton = ink.Cancels(ink.Button("Close", close));
        closeButton.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        var bottom = new StackPanel { Spacing = 6 };
        if (more is not null)
            bottom.Children.Add(more);
        bottom.Children.Add(closeButton);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10, Children = { top, ink.Scroller(_results), bottom } };
        Grid.SetRow((Control)page.Children[1], 1);
        Grid.SetRow(bottom, 2);
        Content = page;
        DetachedFromVisualTree += (_, _) => _search?.Cancel();
    }

    internal TextBox TitleBox => _title;

    internal TextBox ArtistBox => _artist;

    private TextBox Box(string hint)
    {
        var box = new TextBox { PlaceholderText = hint, MinHeight = _ink.Narrow ? 42 : 24, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 200 };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            _ = Search();
        };
        return box;
    }

    private void Put(params Control[] controls)
    {
        _results.Children.Clear();
        _results.Children.AddRange(controls);
    }

    internal async Task Search()
    {
        var title = _title.Text?.Trim() ?? string.Empty;
        var artist = _artist.Text?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            Put(_ink.Label("Type a song title first.", colour: _ink.Sub));
            return;
        }

        _search?.Cancel();
        var mine = _search = new CancellationTokenSource();
        if (!await _context.Allow() || mine.IsCancellationRequested)
            return;
        Put(_ink.Label("Searching MusicBrainz...", bold: true), new ProgressBar { IsIndeterminate = true, Height = 4, MinWidth = 0 },
            _ink.Button("Stop", () =>
            {
                mine.Cancel();
                Put();
            }));
        try
        {
            var result = await _context.Lookup.SearchAsync(title, artist, new LocalSong(title, [], string.Empty, 0), mine.Token);
            if (mine.IsCancellationRequested)
                return;
            _context.Remember(result);
            Show(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (MetadataException ex)
        {
            if (!mine.IsCancellationRequested)
                Put(_ink.Label(ex.Message, colour: _ink.Bad), _ink.Button("Try again", () => _ = Search()));
        }
    }

    private static string Heading(MetadataCandidate candidate) => HistoryText.Pair(candidate.Title, candidate.ArtistText);

    /// <summary>The entry of the same song that is on the most releases: links are most likely there.</summary>
    internal static MetadataCandidate MostReleased(IReadOnlyList<MetadataCandidate> all, MetadataCandidate like) =>
        all.Where(c => TextFold.Key(c.Title) == TextFold.Key(like.Title) && TextFold.Key(c.ArtistText) == TextFold.Key(like.ArtistText))
            .Append(like).MaxBy(c => c.Releases.Count)!;

    private void Show(LookupResult result)
    {
        if (result.Matches.Count == 0)
        {
            Put(_ink.Label("MusicBrainz found nothing for these words.", bold: true),
                _ink.Label("Try the title alone, or another spelling. It covers some music far better than other music.", colour: _ink.Sub));
            return;
        }

        var all = result.Matches.Select(m => m.Candidate).ToList();
        var shown = new List<Control>
        {
            _ink.Label(all.Count > Listed ? $"The first {Listed} of {all.Count} found:" : $"Found ({all.Count}):", bold: true),
        };
        shown.AddRange(all.Take(Listed).Select(c => RowFor(c, MostReleased(all, c))));
        shown.Add(_ink.Label("Information from MusicBrainz.", 11, colour: _ink.Sub));
        Put([.. shown]);
    }

    private Control RowFor(MetadataCandidate candidate, MetadataCandidate mostReleased)
    {
        var parts = new List<string>();
        if (candidate.LengthMs > 0)
            parts.Add(TimeSpan.FromMilliseconds(candidate.LengthMs).ToString(@"m\:ss", CultureInfo.InvariantCulture));
        if (candidate.FirstYear > 0)
            parts.Add(candidate.FirstYear.ToString(CultureInfo.InvariantCulture));
        parts.Add(candidate.Releases.Count == 1 ? "on 1 release" : $"on {candidate.Releases.Count} releases");
        if (MetadataMatcher.Releases(candidate, string.Empty).FirstOrDefault() is { } release)
            parts.Add(release.Title);
        if (candidate.Disambiguation.Length > 0)
            parts.Add(candidate.Disambiguation);

        var buttons = new WrapPanel();
        void Add(Button button)
        {
            button.Margin = new Thickness(0, 0, 8, 4);
            buttons.Children.Add(button);
        }

        Add(_ink.Button("Where to get it...", () => _ = _context.WhereToGet(new LinkTarget(candidate.Id, Heading(candidate)),
            mostReleased.Id == candidate.Id ? null : new LinkTarget(mostReleased.Id, Heading(mostReleased)))));
        if (LookupHistory.PageOf(_context.Lookup.Provider.Name, HistoryMatch.From(candidate)) is { } page)
            Add(_ink.Button("Open on MusicBrainz", () => _ = _context.OpenPage(page, "The song's page on MusicBrainz.")));
        return _ink.Boxed(new StackPanel
        {
            Spacing = 4,
            Children = { _ink.Label(Heading(candidate), bold: true),_ink.Label(string.Join(HistoryText.Dot, parts), 12, colour: _ink.Sub), buttons },
        });
    }
}
