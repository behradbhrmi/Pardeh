using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Pardeh.Core.Duplicates;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.DuplicateViews;

public enum GroupAction
{
    Close,

    /// <summary>Give the kept file the chosen information, then remove the others.</summary>
    KeepAndMerge,

    /// <summary>Remove the others; the kept file's information stays as it is.</summary>
    KeepSelected,

    /// <summary>Take the others out of the library; the files stay.</summary>
    HideExtras,

    NotDuplicates,
    Skip,
}

internal sealed record GroupAnswer(GroupAction Action, GroupChoice? Choice = null);

/// <summary>What can be done with one song from a list of copies.</summary>
internal sealed record SongActions
{
    public Action<SongCopy> Play { get; init; } = _ => { };

    /// <summary>Shows where the file is.</summary>
    public Action<SongCopy> Show { get; init; } = _ => { };

    public string ShowText { get; init; } = "Details";

    public Func<SongCopy, bool> CanFix { get; init; } = _ => false;

    public Func<SongCopy, Task> FixInfo { get; init; } = _ => Task.CompletedTask;

    public Button[] For(CleanupInk ink, SongCopy copy, Action closeFirst)
    {
        var buttons = new List<Button> { ink.Button("Play", () => Play(copy)), ink.Button(ShowText, () => Show(copy)) };
        if (CanFix(copy))
        {
            // Fix Info changes the file, so what is on this screen would be out of date: it closes first.
            buttons.Add(ink.Button("Fix Info...", () =>
            {
                closeFirst();
                _ = FixInfo(copy);
            }));
        }

        return [.. buttons];
    }
}

/// <summary>
/// One group of copies: a card for each, which one to keep, where the kept file's information comes from, and what to do.
/// The same stacked layout serves the desktop and the phone. Nothing is preselected unless there is a clear suggestion.
/// </summary>
internal sealed class GroupView : UserControl
{
    private readonly CleanupInk _ink;
    private readonly DuplicateGroup _group;
    private readonly CopyAdvice _advice;
    private readonly Dictionary<MergeField, int> _sources = [];
    private readonly StackPanel _merge = new() { Spacing = 6 };
    private readonly Button _keepAndMerge, _keepSelected, _hide;
    private int? _keep;

    public GroupView(CleanupInk ink, DuplicateGroup group, CopyAdvice advice, SongActions actions, Action<GroupAnswer> answer)
    {
        _ink = ink;
        _group = group;
        _advice = advice;
        _keep = advice.Keep;

        var cards = new StackPanel { Spacing = 8 };
        cards.Children.Add(ink.Label($"{DuplicateText.Kind(group.Kind)}: {DuplicateText.Count(group.Copies.Count, "copy", "copies")}", 15, bold: true));
        cards.Children.Add(ink.Label(DuplicateText.KindNote(group.Kind), 12, colour: ink.Sub));
        cards.Children.Add(ink.Label((advice.Keep is null ? "No suggestion. " : "Suggested copy is marked. ") + advice.Why, 12));
        var albums = group.Copies.Select(c => CopyAdvisor.Show(c, MergeField.Album)).Where(a => a.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (albums.Count > 1)
            cards.Children.Add(ink.Label("They name different albums (" + string.Join(", ", albums) + "). You may want the song on each.", 12, colour: ink.Bad));

        var name = "keep-" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < group.Copies.Count; i++)
        {
            var index = i;
            var copy = group.Copies[i];
            var pick = new RadioButton
            {
                GroupName = name, IsChecked = _keep == i, MinHeight = ink.Narrow ? 40 : 0,
                Content = ink.Label(advice.Keep == i ? "Keep this one (suggested)" : "Keep this one"),
            };
            pick.IsCheckedChanged += (_, _) =>
            {
                if (pick.IsChecked != true || _keep == index)
                    return;
                _keep = index;
                FillMerge();
            };
            var details = DuplicateText.Details(ink, copy, advice.Notes[i]);
            var buttons = DuplicateText.Buttons(actions.For(ink, copy, () => answer(new GroupAnswer(GroupAction.Close))));
            cards.Children.Add(ink.Boxed(new StackPanel { Spacing = 4, Children = { pick, details, buttons } }));
        }

        cards.Children.Add(_merge);

        Button Answer(string text, GroupAction action, bool primary = false) => ink.Button(text, () =>
        {
            GroupChoice? choice = null;
            if (action is GroupAction.KeepAndMerge or GroupAction.KeepSelected or GroupAction.HideExtras)
            {
                if (_keep is not { } keep)
                    return;
                var others = Enumerable.Range(0, group.Copies.Count).Where(i => i != keep).ToList();
                choice = new GroupChoice(group, keep, others,
                    action == GroupAction.KeepAndMerge ? new Dictionary<MergeField, int>(_sources) : new Dictionary<MergeField, int>());
            }

            answer(new GroupAnswer(action, choice));
        }, primary);

        _keepAndMerge = Answer("Keep and merge...", GroupAction.KeepAndMerge, primary: true);
        _keepSelected = Answer("Remove the others...", GroupAction.KeepSelected);
        _hide = Answer("Hide extras", GroupAction.HideExtras);
        var close = ink.Cancels(ink.Button("Close", () => answer(new GroupAnswer(GroupAction.Close))));
        var bottom = DuplicateText.Buttons(_keepAndMerge, _keepSelected, _hide, Answer("Not duplicates", GroupAction.NotDuplicates),
            Answer("Skip", GroupAction.Skip), close);

        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8, Children = { ink.Scroller(cards), bottom } };
        Grid.SetRow(bottom, 1);
        Content = page;
        FillMerge();
    }

    // "Information for the kept file": one choice for each part that differs between copies.
    private void FillMerge()
    {
        _merge.Children.Clear();
        _sources.Clear();
        var removable = _keep is { } keeping && Enumerable.Range(0, _group.Copies.Count).Any(i => i != keeping && _group.Copies[i].KnownFile);
        _keepSelected.IsEnabled = removable;
        _keepAndMerge.IsEnabled = false;
        _hide.IsEnabled = _keep is not null;
        _keepSelected.Content = "Remove the others...";
        if (_keep is not { } keep)
        {
            _merge.Children.Add(_ink.Label("Choose the copy to keep.", 12, colour: _ink.Sub));
            return;
        }

        var defaults = CopyAdvisor.DefaultSources(_group.Copies, keep);
        var rows = new List<Control>();
        foreach (var field in Enum.GetValues<MergeField>())
        {
            // One entry for each different value; the kept copy's own comes first.
            var choices = new List<(int From, string Text)> { (keep, "Keep: " + Value(_group.Copies[keep], field)) };
            for (var i = 0; i < _group.Copies.Count; i++)
            {
                var copy = _group.Copies[i];
                var shown = CopyAdvisor.Show(copy, field);
                if (i == keep || shown.Length == 0 || Same(copy, _group.Copies[keep], field)
                    || choices.Skip(1).Any(c => Same(copy, _group.Copies[c.From], field)))
                    continue;
                choices.Add((i, $"From copy {i + 1}: {Value(copy, field)}"));
            }

            if (choices.Count == 1)
                continue;
            var box = _ink.Combo();
            box.ItemsSource = choices.Select(c => c.Text).ToList();
            var chosen = defaults.TryGetValue(field, out var from) ? choices.FindIndex(c => Same(_group.Copies[c.From], _group.Copies[from], field)) : 0;
            box.SelectedIndex = Math.Max(0, chosen);
            if (box.SelectedIndex > 0)
                _sources[field] = choices[box.SelectedIndex].From;
            var which = field;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex > 0)
                    _sources[which] = choices[box.SelectedIndex].From;
                else
                    _sources.Remove(which);
                UpdateButtons(removable);
            };
            var label = _ink.Label(FieldName(field), 12, colour: _ink.Sub);
            rows.Add(new StackPanel { Spacing = 2, Children = { label, box } });
        }

        if (rows.Count > 0)
        {
            _merge.Children.Add(_ink.Label($"Information for the kept file (copy {keep + 1})", 14, bold: true));
            _merge.Children.AddRange(rows);
        }

        UpdateButtons(removable);
    }

    private void UpdateButtons(bool removable)
    {
        _keepAndMerge.IsEnabled = removable && _sources.Count > 0;
        _keepAndMerge.IsVisible = _sources.Count > 0;
        _keepSelected.Content = _sources.Count > 0 ? "Remove without merging..." : "Remove the others...";
    }

    private static bool Same(SongCopy a, SongCopy b, MergeField field) =>
        field == MergeField.Cover ? a.Facts.CoverHash == b.Facts.CoverHash : CopyAdvisor.Show(a, field) == CopyAdvisor.Show(b, field);

    private static string Value(SongCopy copy, MergeField field)
    {
        var shown = CopyAdvisor.Show(copy, field);
        return shown.Length == 0 ? "(empty)" : shown;
    }

    private static string FieldName(MergeField field) => field switch
    {
        MergeField.Artists => "Artist",
        MergeField.AlbumArtists => "Album artist",
        MergeField.Track => "Track number",
        MergeField.Genres => "Genre",
        _ => field.ToString(),
    };
}

/// <summary>All that "keep the best of each" would do, group by group, before any of it is done.</summary>
internal sealed class BulkPreviewView : UserControl
{
    public BulkPreviewView(CleanupInk ink, IReadOnlyList<GroupChoice> choices, string destination, Action<bool> answer)
    {
        var removals = choices.Sum(c => c.Remove.Count);
        var bytes = choices.Sum(c => c.Remove.Sum(i => c.Group.Copies[i].Item.FileSize));
        var list = new ListBox { Background = Avalonia.Media.Brushes.Transparent, Padding = new Thickness(0) };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<GroupChoice>((choice, _) =>
        {
            if (choice is null)
                return null;
            var kept = choice.Group.Copies[choice.Keep];
            var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4) };
            panel.Children.Add(ink.Label(DuplicateText.Song(kept) + DuplicateText.Dot + DuplicateText.Kind(choice.Group.Kind).ToLowerInvariant(), 12, bold: true));
            panel.Children.Add(ink.Label("Keep: " + kept.Facts.FileName + DuplicateText.Dot + DuplicateText.Folder(kept.Source), 12));
            foreach (var i in choice.Remove)
            {
                var gone = choice.Group.Copies[i];
                panel.Children.Add(ink.Label("Remove: " + gone.Facts.FileName + DuplicateText.Dot + DuplicateText.Folder(gone.Source), 12, colour: ink.Bad));
            }

            if (choice.Merge.Count > 0)
            {
                panel.Children.Add(ink.Label("Added to the kept file: " + string.Join(", ", choice.Merge.Select(m =>
                    $"{m.Key.ToString().ToLowerInvariant()} ⁨{CopyAdvisor.Show(choice.Group.Copies[m.Value], m.Key)}⁩")), 12, colour: ink.Sub));
            }

            return panel;
        });
        list.AttachedToVisualTree += (_, _) => list.ItemsSource ??= choices;
        if (ink.Narrow)
            ScrollViewer.SetVerticalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);

        var head = ink.Label($"{DuplicateText.Count(choices.Count, "group", "groups")}: keep one copy of each and remove "
            + $"{DuplicateText.Count(removals, "file", "files")} ({DuplicateText.Size(bytes)}) to {destination}. "
            + "Only groups whose copies have the same sound are here.");
        var go = ink.Button($"Remove {DuplicateText.Count(removals, "file", "files")}...", () => answer(true), primary: true);
        var cancel = ink.Cancels(ink.Button("Cancel", () => answer(false)));
        go.HorizontalAlignment = cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(ink.Narrow ? "*,2*" : "*,Auto,Auto"), ColumnSpacing = 8, Children = { cancel, go } };
        Grid.SetColumn(cancel, ink.Narrow ? 0 : 1);
        Grid.SetColumn(go, ink.Narrow ? 1 : 2);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8, Children = { head, list, buttons } };
        Grid.SetRow(list, 1);
        Grid.SetRow(buttons, 2);
        Content = page;
    }
}
