using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Pardeh.Core.Downloads;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.OnlineViews;

/// <summary>What the search of a catalog of free files works with.</summary>
internal sealed record CatalogContext(IDownloadCatalog Catalog, DownloadManager Manager)
{
    /// <summary>True when downloads are on; asks first when they are not.</summary>
    public required Func<Task<bool>> Allow { get; init; }

    /// <summary>Told what a search found, for the history.</summary>
    public Action<string, IReadOnlyList<CatalogFile>> Remember { get; init; } = (_, _) => { };

    public Func<Uri, string, Task> OpenPage { get; init; } = (_, _) => Task.CompletedTask;

    /// <summary>A download was started: the screen that shows it takes over.</summary>
    public Action Started { get; init; } = () => { };
}

/// <summary>
/// A search of freely licensed audio files. A download is offered only where the catalog states a licence that permits it,
/// and the licence and source are shown first and kept with the file.
/// </summary>
internal sealed class CatalogView : UserControl
{
    private readonly CleanupInk _ink;
    private readonly CatalogContext _context;
    private readonly TextBox _words;
    private readonly StackPanel _results = new() { Spacing = 8 };
    private CancellationTokenSource? _search;

    public CatalogView(CleanupInk ink, CatalogContext context, Action close)
    {
        _ink = ink;
        _context = context;
        _words = new TextBox
        {
            PlaceholderText = "Title, composer or performer", MinHeight = ink.Narrow ? 42 : 24, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 200,
        };
        _words.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            _ = Search();
        };
        var go = ink.Button("Search", () => _ = Search(), primary: true);
        go.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var top = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                ink.Label($"Freely licensed audio from {context.Catalog.Name}: much classical, public-domain and open-licence work, and little popular music. "
                    + "The words you type are sent to it.", 12, colour: ink.Sub),
                _words, go,
            },
        };
        var closeButton = ink.Cancels(ink.Button("Close", close));
        closeButton.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        var scroller = ink.Scroller(_results);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10, Children = { top, scroller, closeButton } };
        Grid.SetRow(scroller, 1);
        Grid.SetRow(closeButton, 2);
        Content = page;
        DetachedFromVisualTree += (_, _) => _search?.Cancel();
    }

    internal TextBox WordsBox => _words;

    private void Put(params Control[] controls)
    {
        _results.Children.Clear();
        _results.Children.AddRange(controls);
    }

    internal async Task Search()
    {
        var words = _words.Text?.Trim() ?? string.Empty;
        _search?.Cancel();
        var mine = _search = new CancellationTokenSource();
        if (words.Length == 0)
        {
            Put(_ink.Label("Type what to search for first.", colour: _ink.Sub));
            return;
        }

        if (!await _context.Allow() || mine.IsCancellationRequested)
            return;
        Put(_ink.Label($"Searching {_context.Catalog.Name}...", bold: true), new ProgressBar { IsIndeterminate = true, Height = 4, MinWidth = 0 },
            _ink.Button("Stop", () =>
            {
                mine.Cancel();
                Put();
            }));
        try
        {
            var files = await _context.Catalog.SearchAsync(words, mine.Token);
            if (mine.IsCancellationRequested)
                return;
            _context.Remember(words, files);
            if (files.Count == 0)
            {
                Put(_ink.Label("No audio files were found.", bold: true), _ink.Label("Try fewer words, or another spelling.", colour: _ink.Sub));
                return;
            }

            var shown = new List<Control> { _ink.Label($"Found ({files.Count}). Licences and authors are as the catalog's contributors state them; no one guarantees them.",
                12, colour: _ink.Sub) };
            shown.AddRange(files.Select(RowFor));
            Put([.. shown]);
        }
        catch (OperationCanceledException)
        {
        }
        catch (DownloadException ex)
        {
            if (!mine.IsCancellationRequested)
                Put(_ink.Label(ex.Message, colour: _ink.Bad), _ink.Button("Try again", () => _ = Search()));
        }
    }

    private static string Facts(CatalogFile file)
    {
        var parts = new List<string> { file.TypeName, DownloadText.Size(file.Size > 0 ? file.Size : null) };
        if (file.Seconds > 0)
            parts.Add(TimeSpan.FromSeconds(file.Seconds).ToString(file.Seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture));
        return string.Join(HistoryText.Dot, parts);
    }

    private static Control Row(params Button[] buttons)
    {
        var row = new WrapPanel();
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(button);
        }

        return row;
    }

    private Control RowFor(CatalogFile file)
    {
        var inside = new StackPanel { Spacing = 4 };
        inside.Children.Add(_ink.Label(file.Title, bold: true));
        inside.Children.Add(_ink.Label("Author, as stated: " + (file.Creator.Length > 0 ? file.Creator : "not given"), 12));
        if (file.Attribution.Length > 0)
            inside.Children.Add(_ink.Label("Credit as: " + file.Attribution, 12));
        if (file.Origin.Length > 0)
            inside.Children.Add(_ink.Label("Comes from: " + file.Origin, 12, colour: _ink.Sub));
        inside.Children.Add(_ink.Label("Licence: " + (file.LicenseName.Length > 0 ? file.LicenseName : "not stated"), bold: true));
        var terms = string.Join(" ", new[]
        {
            file.NonCommercial ? "Non-commercial use only." : null,
            file.NoDerivatives ? "No changed versions may be shared." : null,
            file.Restrictions.Length > 0 ? "Also noted: " + file.Restrictions + "." : null,
        }.Where(t => t is not null));
        if (terms.Length > 0)
            inside.Children.Add(_ink.Label(terms, 12));
        inside.Children.Add(_ink.Label(Facts(file), 12, colour: _ink.Sub));

        var buttons = new List<Button>();
        var ask = new StackPanel { Spacing = 4 };
        if (file.CanDownload)
            buttons.Add(_ink.Button("Download...", () => Ask(file, ask)));
        else if (file.WhyNot is { } why)
            inside.Children.Add(_ink.Label("No download here: " + why, 12, colour: _ink.Sub));
        if (file.SourcePage is { } source)
            buttons.Add(_ink.Button("Source page", () => _ = _context.OpenPage(source, $"The file's page on {_context.Catalog.Name}.")));
        if (Core.OnlineInfo.SafeLinks.Check(file.LicenseUrl) is { } licence)
            buttons.Add(_ink.Button("Licence", () => _ = _context.OpenPage(licence, "The text of the licence.")));
        inside.Children.Add(Row([.. buttons]));
        inside.Children.Add(ask);
        return _ink.Boxed(inside);
    }

    private void Ask(CatalogFile file, StackPanel ask)
    {
        if (file.ToOffer(_context.Catalog.Name) is not { } offer)
            return;
        ask.Children.Clear();
        ask.Children.Add(_ink.Label($"Download {offer.FileName} ({DownloadText.Size(offer.Size)}) from {offer.Final.IdnHost}?"
            + (_ink.Narrow ? " The size counts on mobile data too." : string.Empty), bold: true));
        var duty = file.PublicDomain
            ? $"Stated as {file.LicenseName}: saying where it came from is still good practice."
            : $"Under {file.LicenseName}: when you share it, credit the author, name and link the licence, and follow its other terms.";
        ask.Children.Add(_ink.Label(duty + " A recording and the piece it plays can have different rights: what is shown is what the catalog says, and no more.", 12));
        ask.Children.Add(_ink.Label("Saved to: " + _context.Manager.Target.Description, 12, colour: _ink.Sub));
        ask.Children.Add(Row(_ink.Button("Download", async () =>
        {
            if (!await _context.Allow())
                return;
            if (!_context.Manager.Start(offer.ToJob()))
            {
                ask.Children.Clear();
                ask.Children.Add(_ink.Label("Wait for the current download to end, or cancel it.", colour: _ink.Bad));
                return;
            }

            _context.Started();
        }), _ink.Button("Not now", ask.Children.Clear)));
    }
}
