using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace Pardeh.UI.Rendering;

/// <summary>The "Lyrics" page of the phone Settings.</summary>
internal sealed partial class MobileSettingsView
{
    /// <summary>The privacy sentence beside the online switch: what leaves the phone.</summary>
    internal const string OnlinePrivacy = "Sends artist, title, album and duration of the playing track to lrclib.net and to any source you add below. Nothing else.";

    private string LyricsSummary() =>
        !_settings.LyricsEnabled ? "Off" : _settings.LyricsOnline ? "On, with online look-up" : "On, from tags and files";

    private void RefreshLyricsPage()
    {
        var offset = _scroller.Offset;
        Show(LyricsPage());
        _scroller.Offset = offset;
    }

    /// <summary>The user's own lyrics services: a switch per source, two text fields to add one, the ready-made presets and Remove.</summary>
    private Control SourcesCard()
    {
        var rows = new List<Control>();
        foreach (var source in _settings.LyricsSources.ToList())
            rows.Add(Toggle(source.Name, source.Url, source.Enabled, v => { source.Enabled = v; _settings.Save(); }));

        TextBox Field(string placeholder) => new()
        {
            PlaceholderText = placeholder, FontSize = 15, MinHeight = 42, Margin = new Thickness(16, 6), CornerRadius = new CornerRadius(8),
            Foreground = Brush(_t.Ink), VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var name = Field("Name (e.g. My lyrics server)");
        var address = Field("https://example.com/lyrics?artist={artist}&title={title}");
        var message = Text(string.Empty, 13, _t.Sub);
        message.Margin = new Thickness(16, 0, 16, 6);
        message.TextWrapping = Avalonia.Media.TextWrapping.Wrap;

        void Add(string sourceName, string url)
        {
            url = url.Trim();
            if (!Core.LyricsKit.LyricsEndpoint.IsValid(url))
            {
                message.Text = "The address must start with http:// or https:// and contain {title} or {artist}.";
                return;
            }

            if (_settings.LyricsSources.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                message.Text = "That address is already in the list.";
                return;
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                message.Text = "Added. Note: http:// is not encrypted.";
            _settings.LyricsSources.Add(new Core.LyricsKit.LyricsEndpoint { Name = sourceName.Trim().Length > 0 ? sourceName.Trim() : new Uri(url.Replace("{", "").Replace("}", "")).Host, Url = url });
            _settings.Save();
            _host.Lyrics?.Service.InvalidateAll();
            RefreshLyricsPage();
        }

        rows.Add(name);
        rows.Add(address);
        rows.Add(message);
        rows.Add(ButtonRow("Add this source", () => Add(name.Text ?? string.Empty, address.Text ?? string.Empty), accent: true));
        foreach (var preset in Core.LyricsKit.LyricsEndpoint.Presets.Where(p => !_settings.LyricsSources.Any(s => s.Url == p.Url)))
            rows.Add(ButtonRow("Add " + preset.Name, () => Add(preset.Name, preset.Url)));
        if (_settings.LyricsSources.Count > 0)
            rows.Add(ButtonRow("Remove a source...", () =>
            {
                var names = _settings.LyricsSources.Select(s => s.Name).ToList();
                OpenSheet("Remove which source?", [.. names, "Keep them"], names.Count, i =>
                {
                    if (i >= names.Count)
                        return;
                    _settings.LyricsSources.RemoveAt(i);
                    _settings.Save();
                    _host.Lyrics?.Service.InvalidateAll();
                    RefreshLyricsPage();
                });
            }));
        return Card([.. rows]);
    }

    private Control LyricsPage()
    {
        var status = Text(string.Empty, 13, _t.Sub);
        status.Margin = new Thickness(6, 0, 6, 12);
        var lyrics = _host.Lyrics;

        void Clear(Func<LyricsKit.LyricsController, string> action)
        {
            status.Text = lyrics is null ? "Nothing to clear." : action(lyrics);
        }

        var panel = new StackPanel();
        panel.Children.Add(Card(
            Toggle("Show lyrics", "Look for the lyrics of the playing song", _settings.LyricsEnabled, v => { _settings.LyricsEnabled = v; _settings.Save(); }),
            Toggle("Follow while playing", "Keep the current line in view", _settings.LyricsFollow, v => { _settings.LyricsFollow = v; _settings.Save(); }),
            SliderRow("Text size", AppSettings.MinLyricsTextSize, AppSettings.MaxLyricsTextSize, _settings.LyricsTextSize,
                v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%", v => { _settings.LyricsTextSize = Math.Round(v, 2); _settings.Save(); }),
            Toggle("Save fetched lyrics", "Keep lyrics found online on this device, so they show again offline", _settings.LyricsSaveFetched, v => { _settings.LyricsSaveFetched = v; _settings.Save(); })));
        panel.Children.Add(Heading("Chords"));
        panel.Children.Add(Card(
            Toggle("Show chords", "Above the words, from a .cho / .crd file, [Am] marks or your own chords", _settings.LyricsShowChords, v => { _settings.LyricsShowChords = v; _settings.Save(); }),
            Choice("Chord spelling", "How transposed chords are written", ["As written (auto)", "Sharps", "Flats"],
                () => Enum.TryParse<Core.LyricsKit.ChordSpelling>(_settings.ChordSpelling, true, out var spelling) ? (int)spelling : 0,
                i => { _settings.ChordSpelling = ((Core.LyricsKit.ChordSpelling)i).ToString(); _settings.Save(); })));
        panel.Children.Add(Heading("Online"));
        panel.Children.Add(Card(
            Toggle("Look up lyrics online (LRCLIB)", OnlinePrivacy, _settings.LyricsOnline, v => { _settings.LyricsOnline = v; _settings.Save(); })));
        panel.Children.Add(Note("Off by default. Lyrics already on your phone (tags, .lrc files, your own) never leave it."));
        panel.Children.Add(Heading("More sources"));
        panel.Children.Add(SourcesCard());
        panel.Children.Add(Note("Add any web address that returns lyrics as JSON or text. Use {title}, {artist}, {album} and {duration} where the track's details go, e.g. https://api.lyrics.ovh/v1/{artist}/{title}. Only the details the address names are sent, and only while online look-up is on. They are tried after LRCLIB."));
        panel.Children.Add(Heading("Stored lyrics"));
        panel.Children.Add(Card(
            ButtonRow("Clear online lyrics cache", () => Clear(l =>
            {
                l.Service.Store?.ClearFetched();
                l.Service.InvalidateAll();
                l.Refresh();
                return "Online lyrics cache cleared.";
            })),
            ButtonRow("Delete my edited lyrics", () => OpenSheet("Delete all the lyrics you pasted or imported?", ["Delete them", "Keep them"], 1, i =>
            {
                if (i == 0)
                    Clear(l =>
                    {
                        var count = l.Service.Store?.ClearOverrides() ?? 0;
                        l.Service.InvalidateAll();
                        l.Refresh();
                        return count == 1 ? "1 edited lyrics text deleted." : $"{count} edited lyrics texts deleted.";
                    });
            }))));
        panel.Children.Add(status);
        return panel;
    }
}
