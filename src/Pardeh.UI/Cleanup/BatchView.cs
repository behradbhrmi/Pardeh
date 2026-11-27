using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;

namespace Pardeh.UI.CleanupViews;

/// <summary>
/// Clean Up Library: the songs with something to do, in two groups. "Ready" ones hold only safe changes and are ticked;
/// "Needs review" ones are looked at one at a time. Nothing is applied before a summary is shown and agreed to.
/// </summary>
internal sealed class BatchView : UserControl
{
    private sealed record Heading(string Text);

    private sealed record Line(CleanupProposal Proposal);

    private readonly CleanupInk _ink;
    private readonly CleanupScanner _scanner;
    private readonly Func<CleanupProposal, Task<ReviewResult>> _review;
    private readonly Action<IReadOnlyList<CleanupChoice>> _apply;
    private readonly HashSet<string> _unticked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CleanupChoice> _reviewed = new(StringComparer.Ordinal);
    private readonly ListBox _list = new() { Background = Avalonia.Media.Brushes.Transparent, Padding = new Thickness(0) };
    private readonly TextBlock _status;
    private readonly Button _stopScan;
    private readonly CheckBox _rename;
    private readonly ComboBox _patterns;
    private readonly Button _applyButton;
    private readonly Grid _page = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
    private readonly StackPanel _busy = new() { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(20) };
    private List<CleanupProposal> _pending = [];

    public BatchView(CleanupInk ink, CleanupScanner scanner, Func<CleanupProposal, Task<ReviewResult>> review, Func<Task> editPatterns, Func<Task> editRules,
        Action<IReadOnlyList<CleanupChoice>> apply, Action close)
    {
        _ink = ink;
        _scanner = scanner;
        _review = review;
        _apply = apply;
        _status = ink.Label(string.Empty, colour: ink.Sub);
        _stopScan = ink.Button("Stop looking", () => scanner.Cancel());
        var again = ink.Button("Look again", () => _ = scanner.ScanAsync());
        var rules = ink.Button("Junk words...", async () =>
        {
            await editRules();
            Fill();
        });
        var tools = new WrapPanel { Children = { rules, again, _stopScan } };
        foreach (var child in tools.Children)
            child.Margin = new Thickness(0, 0, 8, 4);
        var top = new StackPanel { Spacing = 6, Children = { _status, tools } };

        _list.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            Heading heading => ink.Label(heading.Text, 14, bold: true),
            Line line => RowFor(line.Proposal),
            _ => null,
        });

        if (ink.Narrow)
            ScrollViewer.SetVerticalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);

        _rename = ink.Tick("Rename files too", scanner.Store.RenameFiles);
        _patterns = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = ink.Narrow ? 42 : 24 };
        FillPatterns();
        var patternButton = ink.Button("Patterns...", async () =>
        {
            await editPatterns();
            FillPatterns();
        });
        var naming = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _patterns, patternButton } };
        Grid.SetColumn(patternButton, 1);
        _applyButton = ink.Button("Apply", () => _apply(Choices()), primary: true);
        var closeButton = ink.Cancels(ink.Button("Close", close));
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(ink.Narrow ? "*,2*" : "*,Auto,Auto"), ColumnSpacing = 8, Children = { closeButton, _applyButton } };
        Grid.SetColumn(closeButton, ink.Narrow ? 0 : 1);
        Grid.SetColumn(_applyButton, ink.Narrow ? 1 : 2);
        closeButton.HorizontalAlignment = _applyButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var bottom = new StackPanel { Spacing = 8, Children = { _rename, naming, buttons } };

        Grid.SetRow(_list, 1);
        Grid.SetRow(bottom, 2);
        _page.Children.Add(top);
        _page.Children.Add(_list);
        _page.Children.Add(bottom);
        Content = new Panel { Children = { _page, _busy } };
        Fill();
    }

    private void FillPatterns()
    {
        var all = _scanner.Store.AllPatterns.ToList();
        _patterns.ItemsSource = all;
        _patterns.SelectedItem = all.Contains(_scanner.Store.DefaultPattern) ? _scanner.Store.DefaultPattern : all[0];
    }

    /// <summary>Takes the scan's newest results.</summary>
    public void Fill()
    {
        var progress = _scanner.Progress;
        _stopScan.IsVisible = progress.Running;
        _pending = [.. _scanner.Pending().OrderBy(p => p.FileName, StringComparer.CurrentCultureIgnoreCase)];
        var ready = _pending.Where(p => p.Level == CleanupLevel.Ready).ToList();
        var review = _pending.Where(p => p.Level == CleanupLevel.Review).ToList();
        _status.Text = progress.Running ? $"Still looking: {progress.Done} of {progress.Total} songs. What is found so far is below."
            : !_scanner.Complete ? "Not every song has been looked at yet."
            : _pending.Count == 0 ? "No song needs cleaning up."
            : $"{_pending.Count} {(_pending.Count == 1 ? "song needs" : "songs need")} a look. Ignored: {_scanner.Store.IgnoredCount}.";

        var items = new List<object>();
        if (ready.Count > 0)
        {
            items.Add(new Heading($"Ready ({ready.Count}): safe changes"));
            items.AddRange(ready.Select(p => new Line(p)));
        }

        if (review.Count > 0)
        {
            items.Add(new Heading($"Needs review ({review.Count}): look one at a time"));
            items.AddRange(review.Select(p => new Line(p)));
        }

        _list.ItemsSource = items;
        UpdateApply();
    }

    private static string Describe(CleanupProposal proposal, CleanupChoice? chosen)
    {
        var suggestions = chosen?.Accepted ?? [.. proposal.Suggestions.Where(s => proposal.Level != CleanupLevel.Ready || s.Selected)];
        if (suggestions.Count == 0)
            return chosen is null ? "Details are missing, with nothing to suggest." : "Only the file name.";
        var first = suggestions.FirstOrDefault(s => s.Field == CleanupField.Title) ?? suggestions[0];
        var text = first.Field == CleanupField.Cover ? "Cover: remove a suspected site picture"
            : $"{CleanupInk.FieldName(first)}: {CleanupInk.Shown(first.Current)}  →  {(first.Suggested.Length == 0 ? "removed" : CleanupInk.Shown(first.Suggested))}";
        return suggestions.Count > 1 ? $"{text}  (+{suggestions.Count - 1} more)" : text;
    }

    private Control RowFor(CleanupProposal proposal)
    {
        var chosen = _reviewed.GetValueOrDefault(proposal.Source);
        var isReady = proposal.Level == CleanupLevel.Ready;
        var tick = _ink.Tick(string.Empty, chosen is not null || isReady && !_unticked.Contains(proposal.Source));
        tick.IsEnabled = chosen is not null || isReady;
        tick.IsCheckedChanged += (_, _) =>
        {
            if (tick.IsChecked == true)
            {
                _unticked.Remove(proposal.Source);
            }
            else
            {
                _unticked.Add(proposal.Source);
                if (!isReady)
                    _reviewed.Remove(proposal.Source);
            }

            UpdateApply();
        };
        var detail = _ink.Label(Describe(proposal, chosen), 12, colour: _ink.Sub, wrap: _ink.Narrow);
        if (_ink.Narrow)
            detail.MaxLines = 2;
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _ink.Label(MediaSources.DisplayName(proposal.Source) + (chosen is not null ? "  (reviewed)" : string.Empty), bold: true, wrap: false),
                detail,
            },
        };
        if (chosen is null && isReady && proposal.Suspicions > 0)
            text.Children.Add(_ink.Label($"{proposal.Suspicions} suspected, left alone. Review to see.", 11, colour: _ink.Sub, wrap: _ink.Narrow));
        var open = _ink.Button("Review", async () =>
        {
            var result = await _review(proposal);
            if (result.Answer == ReviewAnswer.Apply && result.Choice is { } choice && (choice.Accepted.Count > 0 || choice.Pattern is not null))
            {
                _reviewed[proposal.Source] = choice;
                _unticked.Remove(proposal.Source);
            }
            else if (result.Answer == ReviewAnswer.Ignore)
            {
                _reviewed.Remove(proposal.Source);
                _scanner.Store.SetIgnored(proposal.Source, true);
            }

            Fill();
        });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Children = { tick, text, open } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(open, 2);
        return row;
    }

    // A reviewed song keeps its own choice about the name; the others follow the box at the bottom.
    private List<CleanupChoice> Choices()
    {
        var pattern = _rename.IsChecked == true ? _patterns.SelectedItem as string : null;
        var choices = new List<CleanupChoice>();
        foreach (var proposal in _pending)
        {
            if (_reviewed.TryGetValue(proposal.Source, out var reviewed))
                choices.Add(reviewed);
            else if (proposal.Level == CleanupLevel.Ready && !_unticked.Contains(proposal.Source))
                choices.Add(new CleanupChoice(proposal.Source, [.. proposal.Suggestions.Where(s => s.Selected)], pattern));
        }

        return choices;
    }

    private void UpdateApply()
    {
        var count = Choices().Count;
        _applyButton.Content = count == 0 ? "Apply" : $"Apply to {count} {(count == 1 ? "song" : "songs")}...";
        _applyButton.IsEnabled = count > 0;
    }

    /// <summary>Shows what is happening instead of the list, with a way to stop.</summary>
    public void ShowBusy(string message, int done, int total, Action? stop)
    {
        _page.IsVisible = false;
        _busy.IsVisible = true;
        _busy.Children.Clear();
        _busy.Children.Add(_ink.Label(message, 15, bold: true));
        _busy.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Value = done, Height = 12 });
        _busy.Children.Add(_ink.Label($"{done} of {total}", colour: _ink.Sub));
        if (stop is not null)
            _busy.Children.Add(_ink.Button("Stop after this file", stop));
    }

    /// <summary>Back to the list.</summary>
    public void ShowList()
    {
        _busy.IsVisible = false;
        _page.IsVisible = true;
        _reviewed.Clear();
        _unticked.Clear();
        Fill();
    }
}
