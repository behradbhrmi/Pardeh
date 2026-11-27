using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.UI.CleanupViews;

/// <summary>File name patterns: presets or one's own, typed or built with the field buttons, previewed on real songs.</summary>
internal sealed class PatternView : UserControl
{
    private static readonly (string Field, string Name)[] Buttons =
    [
        ("title", "Title"), ("artist", "Artist"), ("albumartist", "Album artist"), ("album", "Album"), ("track", "Track"), ("year", "Year"), ("genre", "Genre"),
    ];

    /// <param name="samples">Real songs to preview on: their tags and file names.</param>
    public PatternView(CleanupInk ink, CleanupStore store, IReadOnlyList<(TagFields Tags, string FileName)> samples, Action close)
    {
        var box = new TextBox { Text = store.DefaultPattern, MinHeight = ink.Narrow ? 42 : 26, VerticalContentAlignment = VerticalAlignment.Center };
        var error = ink.Label(string.Empty, colour: ink.Bad);
        var preview = new StackPanel { Spacing = 4 };
        var rename = ink.Tick("Tick \"Rename the file\" by default", store.RenameFiles);

        var saved = new ListBox { MaxHeight = ink.Narrow ? 150 : 130 };
        if (ink.Narrow)
            ScrollViewer.SetVerticalScrollBarVisibility(saved, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);
        void FillSaved()
        {
            saved.ItemsSource = store.AllPatterns.ToList();
            saved.SelectedItem = store.AllPatterns.Contains(box.Text) ? box.Text : null;
        }

        var remove = ink.Button("Remove saved pattern", () =>
        {
            if (saved.SelectedItem is string pattern)
                store.RemovePattern(pattern);
            FillSaved();
        });

        var save = ink.Button("Save as default", () =>
        {
            if (NamePattern.Check(box.Text ?? string.Empty) is null)
            {
                store.SetNaming(box.Text!, rename.IsChecked == true);
                close();
            }
        }, primary: true);

        void Update()
        {
            var pattern = box.Text ?? string.Empty;
            var problem = NamePattern.Check(pattern);
            error.Text = problem ?? string.Empty;
            error.IsVisible = problem is not null;
            save.IsEnabled = problem is null;
            preview.Children.Clear();
            if (samples.Count == 0)
                preview.Children.Add(ink.Label("No songs to preview on.", colour: ink.Sub));
            foreach (var (tags, name) in samples)
            {
                var result = problem is null ? NamePattern.Apply(pattern, tags, Path.GetExtension(name)) : null;
                preview.Children.Add(ink.Label($"{name}  →  {result ?? "(name stays)"}", 12, colour: ink.Sub));
            }

            remove.IsEnabled = saved.SelectedItem is string chosen && !NamePattern.Presets.Contains(chosen);
        }

        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Update();
        };
        saved.SelectionChanged += (_, _) =>
        {
            if (saved.SelectedItem is string pattern && pattern != box.Text)
                box.Text = pattern;
            Update();
        };

        var fields = new WrapPanel();
        foreach (var (field, name) in Buttons)
        {
            var button = ink.Button(name, () =>
            {
                var text = box.Text ?? string.Empty;
                var at = Math.Clamp(box.CaretIndex, 0, text.Length);
                box.Text = text[..at] + "{" + field + "}" + text[at..];
                box.CaretIndex = at + field.Length + 2;
            });
            button.Margin = new Thickness(0, 0, 6, 6);
            fields.Children.Add(button);
        }

        var cancel = ink.Cancels(ink.Button("Cancel", close));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } };

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                ink.Label("Saved patterns", bold: true), saved, remove,
                ink.Label("Pattern", bold: true), box, fields, error,
                ink.Label("Preview", bold: true), preview, rename,
                ink.Label("Renaming never moves a file to another folder, and never overwrites one.", 12, colour: ink.Sub),
            },
        };
        var page = new DockPanel();
        actions.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(actions, Dock.Bottom);
        page.Children.Add(actions);
        page.Children.Add(ink.Scroller(content));
        Content = page;
        FillSaved();
        Update();
    }
}

/// <summary>The user's junk words and the words to keep, and what else cleanup remembers.</summary>
internal sealed class RulesView : UserControl
{
    public RulesView(CleanupInk ink, CleanupStore store, Action close)
    {
        var rules = store.Rules;
        TextBox Lines(IReadOnlyList<string> values) => new()
        {
            Text = string.Join(Environment.NewLine, values), AcceptsReturn = true, Height = ink.Narrow ? 96 : 110,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        var junk = Lines(rules.Junk);
        var keep = Lines(rules.Keep);
        var badges = ink.Tick("Show \"Needs cleanup\" and \"Match found\" marks in the library", store.ShowBadges);
        var forget = ink.Button($"Stop ignoring all songs ({store.IgnoredCount})", () => { });
        forget.IsEnabled = store.IgnoredCount > 0;
        forget.Click += (_, _) =>
        {
            store.ClearIgnored();
            forget.Content = "Stop ignoring all songs (0)";
            forget.IsEnabled = false;
        };

        static string[] Split(TextBox box) => (box.Text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var save = ink.Button("Save", () =>
        {
            var (newJunk, newKeep) = (Split(junk), Split(keep));
            if (badges.IsChecked == true != store.ShowBadges)
                store.SetShowBadges(badges.IsChecked == true);
            if (!newJunk.SequenceEqual(rules.Junk) || !newKeep.SequenceEqual(rules.Keep))
                store.SetRules(newJunk, newKeep);
            close();
        }, primary: true);
        var cancel = ink.Cancels(ink.Button("Cancel", close));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } };

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                ink.Label("Always remove these (one per line)", bold: true),
                ink.Label("For example the name of a site or channel. Web addresses and bitrate labels are found without this.", 12, colour: ink.Sub),
                junk,
                ink.Label("Never touch these (one per line)", bold: true),
                ink.Label("For example a band whose name looks like a web address.", 12, colour: ink.Sub),
                keep, badges, forget,
                ink.Label("All of these checks happen on this device. Nothing is sent anywhere.", 12, colour: ink.Sub),
            },
        };
        var page = new DockPanel();
        actions.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(actions, Dock.Bottom);
        page.Children.Add(actions);
        page.Children.Add(ink.Scroller(content));
        Content = page;
    }
}
