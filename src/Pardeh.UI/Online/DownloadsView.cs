using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Pardeh.Core.Downloads;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.OnlineViews;

/// <summary>What the "Downloads" screen works with and asks the app to do.</summary>
internal sealed record DownloadsContext(DownloadManager Manager)
{
    /// <summary>The downloads switch, which is its own and off by default.</summary>
    public Func<bool> IsOn { get; init; } = () => false;

    /// <summary>True when downloads are on; asks first when they are not.</summary>
    public Func<Task<bool>> Allow { get; init; } = () => Task.FromResult(false);

    /// <summary>Shows the host, asks, and opens the page in the browser.</summary>
    public Func<Uri, string, Task> OpenPage { get; init; } = (_, _) => Task.CompletedTask;

    public Func<Task<string?>> ReadClipboard { get; init; } = () => Task.FromResult<string?>(null);

    public Action<DownloadRecord> Play { get; init; } = _ => { };

    /// <summary>Shows the file in its folder; null where there is no such thing.</summary>
    public Action<DownloadRecord>? Reveal { get; init; }

    public Func<DownloadRecord, bool> IsThere { get; init; } = _ => true;

    /// <summary>Opens the search of free files; null where there is none.</summary>
    public Func<Task>? SearchCatalog { get; init; }
}

/// <summary>How downloads read on screen.</summary>
internal static class DownloadText
{
    public static string Size(long? bytes) => bytes is not { } size ? "size not stated"
        : size < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, size / 1024.0):0} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{size / (1024.0 * 1024.0):0.0} MB");

    public static string State(DownloadStatus status) => status.Stage switch
    {
        DownloadStage.Downloading => status.Total is { } total ? $"Downloading: {Size(status.Done)} of {Size(total)}" : $"Downloading: {Size(status.Done)} so far",
        DownloadStage.Checking => "Checking that the file is complete audio...",
        DownloadStage.Adding => "Adding it...",
        DownloadStage.Paused => status.CanResume ? $"Paused at {Size(status.Done)}." : "Stopped.",
        DownloadStage.Done => "Added to your library.",
        _ => "Not downloaded. " + status.Message,
    };

    public static string From(DownloadRecord record) => record.Credit is { } credit
        ? $"{credit.Catalog}: “{credit.Title}”" + (credit.Creator.Length > 0 ? $" by {credit.Creator}" : string.Empty) + $"\nLicence: {credit.LicenseName}"
        : $"Pasted link: {record.Address}\nLicence: not known";

    /// <summary>What non-commercial and no-derivatives mean, in words.</summary>
    public static string Terms(DownloadCredit credit) => string.Join(" ", new[]
    {
        credit.NonCommercial ? "Non-commercial use only." : null,
        credit.NoDerivatives ? "No changed versions may be shared." : null,
    }.Where(t => t is not null));
}

/// <summary>
/// "Downloads": a pasted link or a free file from a catalog, the download that runs, and what was downloaded with where it
/// came from. Downloads run one at a time, while the app is open.
/// </summary>
internal sealed class DownloadsView : UserControl
{
    private const int Listed = 50;

    private readonly CleanupInk _ink;
    private readonly DownloadsContext _context;
    private readonly StackPanel _start = new() { Spacing = 8 };
    private readonly StackPanel _now = new() { Spacing = 6 };
    private readonly StackPanel _lists = new() { Spacing = 8 };
    private readonly ProgressBar _bar = new() { Height = 8, MinWidth = 0, Minimum = 0 };
    private TextBlock? _state;
    private (string? Id, DownloadStage? Stage) _shown = (null, null);
    private bool? _on;

    public DownloadsView(CleanupInk ink, DownloadsContext context, Action close)
    {
        _ink = ink;
        _context = context;
        var body = new StackPanel { Spacing = 12, Children = { _start, _now, _lists } };
        var closeButton = ink.Cancels(ink.Button("Close", close));
        closeButton.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10, Children = { ink.Scroller(body), closeButton } };
        Grid.SetRow(closeButton, 1);
        Content = page;
        Fill();
    }

    /// <summary>Takes the newest state. The paste box keeps what is in it.</summary>
    public void Fill()
    {
        var on = _context.IsOn();
        if (_on != on)
        {
            _on = on;
            FillStart(on);
            _shown = (null, null);
        }

        var status = _context.Manager.Status;
        if (status is { Stage: DownloadStage.Downloading } && _shown == (status.Job.Id, status.Stage))
        {
            // Only the numbers move.
            _state!.Text = DownloadText.State(status);
            _bar.Value = status.Done;
            return;
        }

        _shown = (status?.Job.Id, status?.Stage);
        FillNow(status);
        FillLists(status);
    }

    private void FillStart(bool on)
    {
        _start.Children.Clear();
        if (!on)
        {
            _start.Children.Add(_ink.Label("Downloads are off.", 15, bold: true));
            _start.Children.Add(_ink.Label("Nothing is downloaded or sent until you turn them on. They have their own switch, also in Settings.", colour: _ink.Sub));
            var turnOn = _ink.Button("Turn on downloads...", async () =>
            {
                await _context.Allow();
                Fill();
            });
            turnOn.HorizontalAlignment = HorizontalAlignment.Left;
            _start.Children.Add(turnOn);
            return;
        }

        if (_context.SearchCatalog is { } search)
        {
            var free = _ink.Button("Search free files on Wikimedia Commons...", () => _ = search());
            free.HorizontalAlignment = _ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            _start.Children.Add(free);
        }

#if PARDEH_LINK_DOWNLOADS
        _start.Children.Add(new PasteLinkPanel(_ink, _context, Fill));
#endif
        _start.Children.Add(_ink.Label($"Files go to: {_context.Manager.Target.Description}. One download at a time, while Pardeh is open.", 12, colour: _ink.Sub));
    }

    private Control Row(params Button[] buttons)
    {
        var row = new WrapPanel();
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(button);
        }

        return row;
    }

    private void FillNow(DownloadStatus? status)
    {
        _now.Children.Clear();
        if (status is null)
            return;
        var inside = new StackPanel { Spacing = 6 };
        inside.Children.Add(_ink.Label(status.Job.FileName, bold: true));
        inside.Children.Add(_ink.Label("From " + status.Job.Host, 12, colour: _ink.Sub));
        _state = _ink.Label(DownloadText.State(status), colour: status.Stage == DownloadStage.Failed ? _ink.Bad : _ink.Text);
        inside.Children.Add(_state);
        if (status.Message is { } note && status.Stage != DownloadStage.Failed)
            inside.Children.Add(_ink.Label(note, 12, colour: _ink.Sub));

        if (status.IsRunning)
        {
            if (_bar.Parent is Panel old)
                old.Children.Remove(_bar);
            _bar.IsIndeterminate = status.Stage != DownloadStage.Downloading || status.Total is null;
            _bar.Maximum = Math.Max(1, status.Total ?? 1);
            _bar.Value = status.Done;
            inside.Children.Add(_bar);
            var cancel = _ink.Button("Cancel", () => _context.Manager.Cancel(status.Job.Id));
            inside.Children.Add(status.Stage == DownloadStage.Downloading ? Row(_ink.Button("Pause", _context.Manager.Pause), cancel) : Row(cancel));
        }
        else if (status.Stage != DownloadStage.Done && status.CanResume)
        {
            inside.Children.Add(Row(_ink.Button("Resume", () => Resume(status.Job.Id)), _ink.Button("Cancel", () =>
            {
                _context.Manager.Cancel(status.Job.Id);
                Fill();
            })));
        }

        _now.Children.Add(_ink.Boxed(inside));
    }

    private async void Resume(string id)
    {
        if (!await _context.Allow())
            return;
        if (_context.Manager.Store.Job(id) is { } job)
            _context.Manager.Start(job);
        Fill();
    }

    private void FillLists(DownloadStatus? status)
    {
        _lists.Children.Clear();
        var waiting = _context.Manager.Store.Unfinished.Where(j => j.Id != status?.Job.Id).ToList();
        if (waiting.Count > 0)
        {
            _lists.Children.Add(_ink.Label("Unfinished", 14, bold: true));
            foreach (var job in waiting)
            {
                var resume = _ink.Button("Resume", () => Resume(job.Id));
                resume.IsEnabled = !_context.Manager.IsBusy;
                _lists.Children.Add(_ink.Boxed(new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        _ink.Label(job.FileName, bold: true), _ink.Label($"From {job.Host}, {DownloadText.Size(job.Size)}", 12, colour: _ink.Sub),
                        Row(resume, _ink.Button("Remove", () =>
                        {
                            _context.Manager.Cancel(job.Id);
                            _shown = (null, null);
                            Fill();
                        })),
                    },
                }));
            }
        }

        var finished = _context.Manager.Store.Finished;
        if (finished.Count == 0)
            return;
        _lists.Children.Add(_ink.Label(finished.Count > Listed ? $"Downloaded (the newest {Listed} of {finished.Count})" : "Downloaded", 14, bold: true));
        foreach (var record in finished.Take(Listed))
            _lists.Children.Add(RecordRow(record));
    }

    private Control RecordRow(DownloadRecord record)
    {
        var there = _context.IsThere(record);
        var inside = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                _ink.Label(record.FileName, bold: true),
                _ink.Label($"{HistoryText.When(record.When)}{HistoryText.Dot}{DownloadText.Size(record.Size)}" + (there ? string.Empty : HistoryText.Dot + "the file is gone"),
                    12, colour: _ink.Sub),
                _ink.Label(DownloadText.From(record), 12, colour: _ink.Sub),
            },
        };
        if (record.Credit is { } credit && DownloadText.Terms(credit) is { Length: > 0 } terms)
            inside.Children.Add(_ink.Label(terms, 12));

        var buttons = new List<Button>();
        if (there)
        {
            buttons.Add(_ink.Button("Play", () => _context.Play(record)));
            if (_context.Reveal is { } reveal)
                buttons.Add(_ink.Button("Show folder", () => reveal(record)));
        }

        if (Core.OnlineInfo.SafeLinks.Check(record.Credit?.SourcePage) is { } source)
            buttons.Add(_ink.Button("Source page", () => _ = _context.OpenPage(source, "The file's page at its source.")));
        if (Core.OnlineInfo.SafeLinks.Check(record.Credit?.LicenseUrl) is { } licence)
            buttons.Add(_ink.Button("Licence", () => _ = _context.OpenPage(licence, "The text of the licence.")));
        buttons.Add(_ink.Button("Take off list", () =>
        {
            _context.Manager.Store.Forget(record.Id);
            _shown = (null, null);
            Fill();
        }));
        inside.Children.Add(Row([.. buttons]));
        return _ink.Boxed(inside);
    }
}
