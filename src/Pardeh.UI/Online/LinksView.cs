using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.OnlineViews;

/// <summary>A recording whose links can be asked for.</summary>
/// <param name="Name">The song, as the screen's heading.</param>
internal sealed record LinkTarget(string RecordingId, string Name);

/// <summary>What the "Where to get it" screen works with.</summary>
internal sealed record LinksContext
{
    /// <summary>Asks for the links. Throws <see cref="MetadataException"/> when the service could not be asked.</summary>
    public required Func<string, CancellationToken, Task<SongLinks>> Fetch { get; init; }

    /// <summary>Whether the user has agreed to online look-ups; asks when not.</summary>
    public required Func<Task<bool>> Allow { get; init; }

    /// <summary>Shows the host, asks, and opens the page in the browser.</summary>
    public required Func<Uri, string, Task> OpenPage { get; init; }
}

/// <summary>
/// "Where to get it": the pages MusicBrainz lists for one recording, by what they offer. Pardeh only opens them in the
/// browser: it downloads nothing from them and reads none of them.
/// </summary>
internal sealed class LinksView : UserControl
{
    private readonly CleanupInk _ink;
    private readonly LinksContext _context;
    private readonly LinkTarget? _other;
    private readonly StackPanel _body = new() { Spacing = 8 };
    private LinkTarget _target;
    private CancellationTokenSource? _asking;

    /// <param name="other">Another entry of the same song that is on more releases, to try when this one has no links.</param>
    public LinksView(CleanupInk ink, LinksContext context, LinkTarget target, Action close, LinkTarget? other = null)
    {
        _ink = ink;
        _context = context;
        _target = target;
        _other = other;
        var closeButton = ink.Cancels(ink.Button("Close", close));
        closeButton.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10, Children = { ink.Scroller(_body), closeButton } };
        Grid.SetRow(closeButton, 1);
        Content = page;
        DetachedFromVisualTree += (_, _) => _asking?.Cancel();
    }

    /// <summary>Asks for the links, once the user has agreed to online look-ups.</summary>
    public async Task Ask()
    {
        _asking?.Cancel();
        var mine = _asking = new CancellationTokenSource();
        var target = _target;
        Put(_ink.Label(target.Name, 15, bold: true));
        if (!await _context.Allow())
        {
            Put(_ink.Label(target.Name, 15, bold: true), _ink.Label("Online look-ups are off, so nothing was asked.", colour: _ink.Sub),
                _ink.Button("Ask MusicBrainz...", () => _ = Ask()));
            return;
        }

        if (mine.IsCancellationRequested)
            return;
        Put(_ink.Label(target.Name, 15, bold: true), _ink.Label("Asking MusicBrainz for links..."),
            new ProgressBar { IsIndeterminate = true, Height = 4, MinWidth = 0 });
        try
        {
            var links = await _context.Fetch(target.RecordingId, mine.Token);
            if (!mine.IsCancellationRequested)
                Show(target, links);
        }
        catch (OperationCanceledException)
        {
        }
        catch (MetadataException ex)
        {
            if (!mine.IsCancellationRequested)
                Put(_ink.Label(target.Name, 15, bold: true), _ink.Label(ex.Message, colour: _ink.Bad), _ink.Button("Try again", () => _ = Ask()));
        }
    }

    private void Put(params Control[] controls)
    {
        _body.Children.Clear();
        _body.Children.AddRange(controls);
    }

    internal static string Heading(SongLinkKind kind) => kind switch
    {
        SongLinkKind.FreeDownloadPage => "Free download page",
        SongLinkKind.Buy => "Buy",
        SongLinkKind.Stream => "Stream (free)",
        _ => "Stream (subscription)",
    };

    private void Show(LinkTarget target, SongLinks links)
    {
        var shown = new List<Control> { _ink.Label(target.Name, 15, bold: true) };
        if (links.Links.Count == 0)
        {
            shown.Add(_ink.Label("MusicBrainz lists no links for this entry. That does not mean the song can't be bought or heard: "
                + "links sit on particular entries, and another match may have them."));
            if (_other is { } other && other.RecordingId != target.RecordingId)
                shown.Add(_ink.Button("Try the entry on the most releases", () =>
                {
                    _target = other;
                    _ = Ask();
                }));
        }
        else
        {
            shown.Add(_ink.Label("Listed on MusicBrainz, which anyone can edit. Each opens in your browser; Pardeh downloads nothing from these pages.",
                12, colour: _ink.Sub));
            foreach (var group in links.Links.GroupBy(l => l.Kind).OrderBy(g => g.Key))
            {
                shown.Add(_ink.Label(Heading(group.Key), 14, bold: true));
                foreach (var link in group)
                    shown.Add(RowFor(link));
            }
        }

        if (links.Releases > links.ReleasesSeen)
            shown.Add(_ink.Label($"Looked at {links.ReleasesSeen} of {links.Releases} releases.", 12, colour: _ink.Sub));
        if (links.Insecure > 0)
            shown.Add(_ink.Label(links.Insecure == 1 ? "1 listed link is not secure (no https) and is left out."
                : $"{links.Insecure} listed links are not secure (no https) and are left out.", 12, colour: _ink.Sub));
        Put([.. shown]);
    }

    private Control RowFor(SongLink link)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _ink.Label(link.Site, wrap: false) } };
        text.Children.Add(_ink.Label(link.Release is { } release ? "on " + release : "on the recording", 12, colour: _ink.Sub, wrap: false));
        var open = _ink.Button("Open", () =>
        {
            if (SafeLinks.Check(link.Address) is { } page)
                _ = _context.OpenPage(page, "Listed on MusicBrainz as: " + Heading(link.Kind) + ".");
        });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { text, open } };
        Grid.SetColumn(open, 1);
        return _ink.Boxed(row);
    }
}
