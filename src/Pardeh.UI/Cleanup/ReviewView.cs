using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.UI.CleanupViews;

internal enum ReviewAnswer
{
    Cancel,
    Apply,
    Ignore,
}

internal sealed record ReviewResult(ReviewAnswer Answer, CleanupChoice? Choice = null);

/// <summary>
/// Fix Info for one song: every suggestion with its own tick and an editable value, the cover, and the file name. Wide
/// screens show a table, a phone shows stacked cards. It only asks; nothing is written here.
/// </summary>
internal sealed class ReviewView : UserControl
{
    private sealed record Row(CleanupSuggestion Suggestion, CheckBox Tick, TextBox? Box);

    private readonly CleanupInk _ink;
    private readonly CleanupProposal _proposal;
    private readonly TagFields _current;
    private readonly CleanupStore _store;
    private readonly List<Row> _rows = [];
    private readonly CheckBox _rename;
    private readonly ComboBox _patterns;
    private readonly TextBlock _newName;

    /// <param name="current">The file's tags now, with pictures.</param>
    /// <param name="editPatterns">Opens the pattern editor; the list here follows what it saved.</param>
    public ReviewView(CleanupInk ink, CleanupProposal proposal, TagFields current, CleanupStore store, Func<Task> editPatterns, Action<ReviewResult> done)
    {
        _ink = ink;
        _proposal = proposal;
        _current = current;
        _store = store;

        var list = new StackPanel { Spacing = ink.Narrow ? 8 : 4, Margin = new Thickness(0, 0, ink.Narrow ? 0 : 18, 0) }; // clear of the scroll bar
        if (!ink.Narrow)
            list.Children.Add(Table(null, ink.Label("Field", bold: true), ink.Label("Now", bold: true), ink.Label("Suggested (you can edit it)", bold: true)));

        var suggestions = proposal.Suggestions.ToList();
        // What is missing and could not be guessed can still be typed in.
        foreach (var (field, value) in new[] { (CleanupField.Title, current.Title ?? string.Empty), (CleanupField.Artists, string.Join("; ", current.Artists ?? [])) })
            if (proposal.MissingDetails && FileNameReader.IsPlaceholder(value) && suggestions.All(s => s.Field != field))
                suggestions.Insert(field == CleanupField.Title ? 0 : Math.Min(1, suggestions.Count),
                    new CleanupSuggestion(field, null, value, string.Empty, CleanupReason.FromFileName, false));

        foreach (var suggestion in suggestions.Where(s => s.Field is not (CleanupField.Text or CleanupField.Cover)))
            list.Children.Add(Field(suggestion));
        if (suggestions.FirstOrDefault(s => s.Field == CleanupField.Cover) is { } cover)
            list.Children.Add(Cover(cover));
        var texts = suggestions.Where(s => s.Field == CleanupField.Text).ToList();
        if (texts.Count > 0)
            list.Children.Add(OtherTexts(texts));
        if (suggestions.Count == 0)
            list.Children.Add(ink.Label("No website text was found in this song. You can still rename the file.", colour: ink.Sub));

        _rename = ink.Tick("Rename the file", store.RenameFiles);
        _patterns = new ComboBox { MinWidth = ink.Narrow ? 0 : 260, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = ink.Narrow ? 42 : 24 };
        FillPatterns(store.DefaultPattern);
        _newName = ink.Label(string.Empty, colour: ink.Sub);
        _rename.IsCheckedChanged += (_, _) => Preview();
        _patterns.SelectionChanged += (_, _) => Preview();
        var edit = ink.Button("Patterns...", async () =>
        {
            await editPatterns();
            FillPatterns(_store.DefaultPattern);
            Preview();
        });
        var pick = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _patterns, edit } };
        Grid.SetColumn(edit, 1);
        list.Children.Add(ink.Boxed(new StackPanel { Spacing = 6, Children = { _rename, ink.Label("Now: " + proposal.FileName, colour: ink.Sub), _newName, pick } }));

        var level = proposal.Level switch
        {
            CleanupLevel.Clean => "Nothing to clean up.",
            CleanupLevel.Ready => "Only safe changes are ticked.",
            _ => "Please check: what is not certain is left unticked.",
        };
        var header = new StackPanel { Spacing = 2, Children = { ink.Label(proposal.FileName, ink.Narrow ? 15 : 13, bold: true, wrap: false), ink.Label(level, colour: ink.Sub) } };

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(ink.Narrow ? "*,*,*" : "Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        var ignore = ink.Button("Ignore song", () => done(new ReviewResult(ReviewAnswer.Ignore)));
        var cancel = ink.Button("Cancel", () => done(new ReviewResult(ReviewAnswer.Cancel)));
        cancel.IsCancel = true;
        var apply = ink.Button(ink.Narrow ? "Apply" : "Apply selected", () => done(new ReviewResult(ReviewAnswer.Apply, Choice())), primary: true);
        var column = 0;
        foreach (var button in new[] { ignore, cancel, apply })
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(button, ink.Narrow ? column : column == 0 ? 0 : column + 1);
            column++;
            buttons.Children.Add(button);
        }

        var page = new DockPanel { LastChildFill = true };
        header.Margin = new Thickness(0, 0, 0, 10);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        page.Children.Add(header);
        page.Children.Add(buttons);
        page.Children.Add(ink.Scroller(list));
        Content = page;
        Preview();
    }

    private static Grid Table(Control? tick, Control field, Control now, Control suggested)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,110,*,*"), ColumnSpacing = 8 };
        Control?[] cells = [tick, field, now, suggested];
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not { } cell)
                continue;
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private Control Field(CleanupSuggestion suggestion)
    {
        var tick = _ink.Tick(string.Empty, suggestion.Selected);
        var box = new TextBox
        {
            Text = suggestion.Suggested, PlaceholderText = suggestion.Suggested.Length == 0 ? "(remove)" : string.Empty,
            MinHeight = _ink.Narrow ? 40 : 24, VerticalContentAlignment = VerticalAlignment.Center,
        };
        // Typing a value means wanting it.
        // The property, not TextChanged: that event arrives later, after a quick tap on Apply.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty)
                return;
            if (box.Text != suggestion.Suggested)
                tick.IsChecked = true;
            Preview();
        };
        tick.IsCheckedChanged += (_, _) => Preview();
        _rows.Add(new Row(suggestion, tick, box));

        var name = CleanupInk.FieldName(suggestion);
        var why = suggestion.Suggested.Length == 0 && suggestion.Current.Length == 0 ? "Missing: type it here" : CleanupInk.Why(suggestion.Reason);
        var now = _ink.Label(CleanupInk.Shown(suggestion.Current), colour: _ink.Sub);
        if (_ink.Narrow)
        {
            tick.Content = _ink.Label(name, bold: true);
            return _ink.Boxed(new StackPanel
            {
                Spacing = 4,
                Children = { tick, _ink.Label("Now: " + CleanupInk.Shown(suggestion.Current), colour: _ink.Sub), box, _ink.Label(why, 12, colour: _ink.Sub) },
            });
        }

        ToolTip.SetTip(box, why);
        var suggested = new StackPanel { Spacing = 2, Children = { box, _ink.Label(why, 11, colour: _ink.Sub) } };
        return Table(tick, _ink.Label(name), now, suggested);
    }

    private Control Cover(CleanupSuggestion suggestion)
    {
        var tick = _ink.Tick("Remove this cover picture", suggestion.Selected);
        _rows.Add(new Row(suggestion, tick, null));
        var inside = new StackPanel { Spacing = 6, Children = { tick, _ink.Label(CleanupInk.Why(suggestion.Reason), 12, colour: _ink.Sub) } };
        var picture = _current.Pictures?.FirstOrDefault(p => CleanupAnalyzer.HashCover(p.Data) == suggestion.CoverHash);
        if (picture is not null)
        {
            try
            {
                using var stream = new MemoryStream(picture.Data);
                var image = new Image { Source = Bitmap.DecodeToWidth(stream, 192), Width = 96, Height = 96, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                inside.Children.Insert(0, image);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A picture that can't be drawn is still offered for removal.
            }
        }

        return _ink.Boxed(inside);
    }

    // The other text fields are one tick, as one job, with what it covers listed under it.
    private Control OtherTexts(List<CleanupSuggestion> texts)
    {
        var tick = _ink.Tick($"Remove website text from other fields ({texts.Count} found)", texts.All(t => t.Selected));
        foreach (var text in texts)
            _rows.Add(new Row(text, tick, null));
        var inside = new StackPanel { Spacing = 4, Children = { tick } };
        foreach (var text in texts)
            inside.Children.Add(_ink.Label($"{CleanupInk.FieldName(text)}: {CleanupInk.Shown(text.Current)}  →  {(text.Suggested.Length == 0 ? "removed" : CleanupInk.Shown(text.Suggested))}", 12, colour: _ink.Sub));
        return _ink.Boxed(inside);
    }

    private void FillPatterns(string selected)
    {
        var all = _store.AllPatterns.ToList();
        _patterns.ItemsSource = all;
        _patterns.SelectedItem = all.Contains(selected) ? selected : all[0];
    }

    private List<CleanupSuggestion> Accepted() =>
    [
        .. _rows.Where(r => r.Tick.IsChecked == true)
            .Select(r => r.Box is { } box ? r.Suggestion with { Suggested = box.Text?.Trim() ?? string.Empty } : r.Suggestion)
            // A row left blank for something that was blank changes nothing.
            .Where(s => s.Field is CleanupField.Cover || s.Suggested != s.Current),
    ];

    private string? Pattern => _rename?.IsChecked == true ? _patterns.SelectedItem as string : null;

    private CleanupChoice Choice() => new(_proposal.Source, Accepted(), Pattern);

    private void Preview()
    {
        if (_newName is null || _patterns.SelectedItem is not string pattern)
            return;
        var change = CleanupAnalyzer.ToChange(_current, Accepted(), CleanupAnalyzer.HashCover);
        var name = CleanupPlanner.NewName(pattern, _current, _current.Differences(change), _proposal.FileName);
        _newName.Text = name is null ? "No title, so the name stays." : "New: " + name;
        _newName.Opacity = _rename.IsChecked == true ? 1 : 0.6;
    }
}
