using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.OnlineInfo;

namespace Pardeh.UI.CleanupViews;

/// <summary>What the review screen needs to look its song up online.</summary>
/// <param name="Steps">The searches to try, in order.</param>
/// <param name="Song">The song on this device, for a search typed by hand.</param>
/// <param name="Allow">Whether the user has agreed to online look-ups; asks when not.</param>
/// <param name="DatabaseScript">The user's choice to take names in the database's script.</param>
internal sealed record OnlineContext(MetadataLookup Lookup, IReadOnlyList<LookupStep> Steps, LocalSong Song, Func<Task<bool>> Allow, Func<bool> DatabaseScript)
{
    /// <summary>The saved result of an earlier look-up, shown without asking again. Only given while look-ups are on.</summary>
    public OnlineEntry? Saved { get; init; }

    /// <summary>Told what a look-up of the song's own details found (not a search typed by hand).</summary>
    public Action<LookupResult> Remember { get; init; } = _ => { };

    /// <summary>Told what a search typed by hand found, for the history.</summary>
    public Action<LookupResult> RememberTyped { get; init; } = _ => { };

    /// <summary>Opens "Where to get it" for a recording; null where it can't be.</summary>
    public Action<MetadataCandidate>? WhereToGet { get; init; }
}

/// <summary>
/// The online part of Fix Info: starting a look-up, the match and why, the other matches and releases, and a search by hand.
/// Nothing is sent before the button is pressed and the user has agreed.
/// </summary>
internal sealed class OnlineView : UserControl
{
    private const int ListedMatches = 25;

    private readonly CleanupInk _ink;
    private readonly OnlineContext _context;
    private readonly CleanupProposal _local;
    private readonly TagFields _current;
    private readonly Action<CleanupProposal> _show;
    private readonly Action<CleanupProposal> _showCover;
    private readonly StackPanel _inside = new() { Spacing = 6 };
    private readonly Dictionary<string, CoverImage?> _covers = [];

    // One thing at a time: a new search or choice cancels the one before, whose answer would no longer be wanted.
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _cover;
    private bool _asking;
    private string _typedTitle;
    private string _typedArtist;

    /// <param name="show">Shows these suggestions in place of the ones on screen.</param>
    /// <param name="showCover">Takes only the cover suggestion from these, leaving what the user typed elsewhere.</param>
    public OnlineView(CleanupInk ink, OnlineContext context, CleanupProposal local, TagFields current, Action<CleanupProposal> show, Action<CleanupProposal> showCover)
    {
        _ink = ink;
        _context = context;
        _local = local;
        _current = current;
        _show = show;
        _showCover = showCover;
        _typedTitle = context.Steps.Count > 0 ? context.Steps[0].Search.Title : context.Song.Title;
        _typedArtist = context.Steps.Count > 0 ? context.Steps[0].Search.Artist ?? string.Empty : string.Empty;
        Content = ink.Boxed(_inside);
        DetachedFromVisualTree += (_, _) => Stop();
        ShowStart();
    }

    /// <summary>The match whose details are among the suggestions on screen, or null.</summary>
    public OnlineUse? Offered { get; private set; }

    /// <summary>
    /// Shows the saved result, if there is one. Its cover is then fetched, as for any result on screen. Call it once the
    /// screen is ready to take suggestions.
    /// </summary>
    public void ShowSaved()
    {
        if (_context.Saved is not { } saved)
            return;
        var looked = "Looked up on " + saved.LookedUp.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ".";
        ShowResult(new LookupResult(saved.Matches(), saved.Searched), saved.Song, looked);
    }

    /// <summary>Gives up on whatever is being fetched.</summary>
    public void Stop()
    {
        _search?.Cancel();
        _cover?.Cancel();
    }

    private void Put(params Control[] controls)
    {
        _inside.Children.Clear();
        _inside.Children.AddRange(controls);
    }

    private Control Buttons(params Button[] buttons)
    {
        var row = new WrapPanel();
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(button);
        }

        return row;
    }

    private Button ByHand() => _ink.Button("Search by hand...", () => _ = Typed());

    private void ShowStart()
    {
        if (_context.Steps.Count == 0)
        {
            Put(_ink.Label("There is no title to search for. You can search by hand.", colour: _ink.Sub), Buttons(ByHand()));
            return;
        }

        Put(_ink.Label("The right details and cover may be online.", colour: _ink.Sub),
            Buttons(_ink.Button("Look up online", () => _ = Look()), ByHand()));
    }

    /// <summary>Looks the song up, once the user has agreed to online look-ups.</summary>
    internal Task Look() => Run(() => _context.Lookup.FindAsync(_context.Steps, _search!.Token), Look, remember: true);

    private async Task Typed()
    {
        if (!await Allowed())
            return;
        Stop();
        var title = new TextBox { Text = _typedTitle, PlaceholderText = "Title", MinHeight = _ink.Narrow ? 40 : 24, VerticalContentAlignment = VerticalAlignment.Center };
        var artist = new TextBox { Text = _typedArtist, PlaceholderText = "Artist (may be left empty)", MinHeight = _ink.Narrow ? 40 : 24, VerticalContentAlignment = VerticalAlignment.Center };
        var go = _ink.Button("Search", () =>
        {
            _typedTitle = title.Text?.Trim() ?? string.Empty;
            _typedArtist = artist.Text?.Trim() ?? string.Empty;
            if (_typedTitle.Length > 0)
                _ = SearchTyped();
        });
        Put(_ink.Label("Search MusicBrainz for:", bold: true), title, artist, Buttons(go, _ink.Button("Back", ShowStart)));
    }

    private Task SearchTyped() => Run(() => _context.Lookup.SearchAsync(_typedTitle, _typedArtist, _context.Song, _search!.Token), SearchTyped);

    private async Task<bool> Allowed()
    {
        if (_asking)
            return false;
        _asking = true;
        try
        {
            return await _context.Allow();
        }
        finally
        {
            _asking = false;
        }
    }

    private async Task Run(Func<Task<LookupResult>> search, Func<Task> again, bool remember = false)
    {
        if (!await Allowed())
            return;
        Stop();
        var mine = _search = new CancellationTokenSource();
        Put(_ink.Label("Searching MusicBrainz...", bold: true), new ProgressBar { IsIndeterminate = true, Height = 4, MinWidth = 0 },
            Buttons(_ink.Button("Stop", () =>
            {
                mine.Cancel();
                ShowStart();
            })));
        try
        {
            var result = await search();
            if (mine.IsCancellationRequested)
                return;
            if (remember)
                _context.Remember(result);
            else
                _context.RememberTyped(result);
            ShowResult(result, _context.Song, null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (MetadataException ex)
        {
            if (!mine.IsCancellationRequested)
                Put(_ink.Label(ex.Message, colour: _ink.Bad), Buttons(_ink.Button("Try again", () => _ = again()), ByHand()));
        }
    }

    private static string Searched(MetadataSearch search) => "Searched for: " + search.Title + (search.Artist is { } artist ? " - " + artist : " (title only)");

    private static string Length(int milliseconds) =>
        milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds).ToString(@"m\:ss", CultureInfo.InvariantCulture) : "?:??";

    // The invisible left-to-right mark keeps the parts in order when some are Persian: without it the numbers after a
    // Persian name are laid out before it.
    private const string Dot = "‎ · ";

    private string Describe(MetadataMatch match)
    {
        var candidate = match.Candidate;
        var name = candidate.Title + " - " + candidate.ArtistText;
        var parts = new List<string> { match.Level.ToString(), Length(candidate.LengthMs) };
        if (candidate.FirstYear is var year and > 0)
            parts.Add(year.ToString(CultureInfo.InvariantCulture));
        if (candidate.Disambiguation.Length > 0)
            parts.Add(candidate.Disambiguation);
        // A phone is too narrow for one line, and what tells the entries apart must not be what is cut off.
        if (_ink.Narrow)
            return string.Join(Dot, parts) + "\n" + name;
        parts.Insert(1, name);
        return string.Join(Dot, parts);
    }

    private static string Describe(MetadataRelease release)
    {
        var kind = release.SecondaryTypes.Count > 0 ? string.Join(", ", release.SecondaryTypes) : release.PrimaryType;
        var parts = new List<string> { release.Title };
        if (release.Year > 0)
            parts.Add(release.Year.ToString(CultureInfo.InvariantCulture));
        if (kind is not null)
            parts.Add(kind);
        if (!release.Official)
            parts.Add("not official");
        return string.Join(Dot, parts);
    }

    private ComboBox Choice(IEnumerable<string> items)
    {
        var box = _ink.Combo();
        box.ItemsSource = items.ToList();
        box.SelectedIndex = 0;
        return box;
    }

    /// <param name="read">The song as the search read it.</param>
    /// <param name="saved">For a saved result, when it was looked up; it can then be looked up again.</param>
    private void ShowResult(LookupResult result, LocalSong read, string? saved)
    {
        var searched = result.Searched is { } search ? _ink.Label(Searched(search), 12, colour: _ink.Sub) : null;
        Button[] Actions() => saved is null ? [ByHand()] : [_ink.Button("Look up again", () => _ = Look()), ByHand()];
        MetadataCandidate? chosen = null;
        Button[] ActionsWithLinks() => _context.WhereToGet is { } where
            ? [_ink.Button("Where to get it...", () =>
            {
                if (chosen is not null)
                    where(chosen);
            }), .. Actions()]
            : Actions();
        if (result.Matches.Count == 0)
        {
            var none = new List<Control> { _ink.Label("No match on MusicBrainz.", bold: true), _ink.Label("The fixes made on this device are below.", colour: _ink.Sub) };
            if (saved is not null)
                none.Add(_ink.Label(saved, 12, colour: _ink.Sub));
            if (searched is not null)
                none.Add(searched);
            none.Add(Buttons(Actions()));
            Put([.. none]);
            Offered = null;
            _show(_local);
            return;
        }

        var matches = result.Matches.Take(ListedMatches).ToList();
        var level = _ink.Label(string.Empty, 15, bold: true);
        var reason = _ink.Label(string.Empty, colour: _ink.Sub);
        var matchBox = Choice(matches.Select(Describe));
        var releaseBox = _ink.Combo();
        var cover = _ink.Label(string.Empty, 12, colour: _ink.Sub);
        IReadOnlyList<MetadataRelease> releases = [];
        var filling = false;

        void ChooseRelease()
        {
            var match = matches[Math.Max(0, matchBox.SelectedIndex)];
            var release = releaseBox.SelectedIndex >= 0 && releaseBox.SelectedIndex < releases.Count ? releases[releaseBox.SelectedIndex] : null;
            Offered = new OnlineUse(match.Candidate, read);
            _show(Merged(matches, match, release, null, read));
            _ = FetchCover(matches, match, release, read, cover);
        }

        void ChooseMatch()
        {
            var match = matches[Math.Max(0, matchBox.SelectedIndex)];
            chosen = match.Candidate;
            level.Text ="Match: " + (match.Level == MatchLevel.None ? "None" : match.Level.ToString());
            reason.Text = match.Reason + (match.Level == MatchLevel.Strong ? "." : ". Check before ticking anything.");
            releases = MetadataMatcher.Releases(match.Candidate, _context.Song.Album);
            filling = true;
            releaseBox.ItemsSource = releases.Select(Describe).ToList();
            releaseBox.SelectedIndex = releases.Count > 0 ? 0 : -1;
            releaseBox.IsVisible = releases.Count > 0;
            filling = false;
            ChooseRelease();
        }

        matchBox.SelectionChanged += (_, _) => ChooseMatch();
        releaseBox.SelectionChanged += (_, _) =>
        {
            if (!filling && releaseBox.SelectedIndex >= 0)
                ChooseRelease();
        };

        var shown = new List<Control> { level, reason };
        if (saved is not null)
            shown.Add(_ink.Label(saved, 12, colour: _ink.Sub));
        if (searched is not null)
            shown.Add(searched);
        shown.AddRange(
        [
            _ink.Label(matches.Count > 1 ? $"Other matches ({matches.Count})" : "The only match", bold: true), matchBox,
            _ink.Label("Album and cover from", bold: true), releaseBox, cover, Buttons(ActionsWithLinks()),
            _ink.Label("Information from MusicBrainz. Covers from the Cover Art Archive belong to their owners.", 11, colour: _ink.Sub),
        ]);
        Put([.. shown]);
        ChooseMatch();
    }

    private CleanupProposal Merged(IReadOnlyList<MetadataMatch> matches, MetadataMatch match, MetadataRelease? release, CoverImage? cover, LocalSong read) =>
        OnlineSuggestions.Merge(_local, _current, match, release, cover, _context.DatabaseScript(),
            OnlineJudge.Doubts(_local, _current, read, matches, match, release));

    private async Task FetchCover(IReadOnlyList<MetadataMatch> matches, MetadataMatch match, MetadataRelease? release, LocalSong read, TextBlock status)
    {
        _cover?.Cancel();
        var mine = _cover = new CancellationTokenSource();
        if (release is null)
        {
            status.Text = string.Empty;
            return;
        }

        try
        {
            if (!_covers.TryGetValue(release.Id, out var image))
            {
                status.Text = "Getting the cover...";
                image = await _context.Lookup.Provider.CoverAsync(release, mine.Token);
                _covers[release.Id] = image;
            }

            if (mine.IsCancellationRequested)
                return;
            status.Text = image is null ? "No cover in the Cover Art Archive for this one." : string.Empty;
            if (image is not null)
                _showCover(Merged(matches, match, release, image, read));
        }
        catch (OperationCanceledException)
        {
        }
        catch (MetadataException ex)
        {
            if (!mine.IsCancellationRequested)
                status.Text = "No cover: " + ex.Message;
        }
    }
}
