using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>The desktop Lyrics and Playback pages (the phone looks have their own in <see cref="MobileSettingsView"/>).</summary>
internal static partial class OptionsDialog
{
    private sealed partial class SettingsBuilder
    {
        private Control PlaybackPage()
        {
            var minutes = MediaPlayer.SleepPresetMinutes.ToList();
            int SleepIndex() => player.SleepAtEndOfTrack ? minutes.Count + 1
                : player.SleepRemaining is { } left ? 1 + Math.Max(0, minutes.FindIndex(m => m >= left.TotalMinutes - 0.01))
                : 0;
            var sleep = Choice(["Off", .. minutes.Select(m => $"{m} minutes"), "At the end of this track"], SleepIndex(), i =>
            {
                if (i == 0)
                    player.CancelSleepTimer();
                else if (i > minutes.Count)
                    player.StartSleepAtEndOfTrack();
                else
                    player.StartSleepTimer(TimeSpan.FromMinutes(minutes[i - 1]));
            });
            var eq = player.Equalizer;
            var bands = new Slider[EqualizerSettings.BandCount];
            var bandRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 6, 0, 0) };
            var showing = false; // true while the sliders are being set from a preset
            for (var i = 0; i < bands.Length; i++)
            {
                var band = i;
                var slider = new Slider
                {
                    Orientation = Orientation.Vertical, Minimum = EqualizerSettings.MinGain, Maximum = EqualizerSettings.MaxGain,
                    Value = eq.GetGain(i), Height = 130, Width = 44, HorizontalAlignment = HorizontalAlignment.Center,
                };
                slider.PropertyChanged += (_, e) =>
                {
                    if (showing || e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                        return;
                    eq.SetGain(band, slider.Value);
                    eq.Bypass = false;
                };
                bands[i] = slider;
                var hz = EqualizerSettings.BandFrequencies[i];
                var label = Note(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}");
                label.HorizontalAlignment = HorizontalAlignment.Center;
                bandRow.Children.Add(new StackPanel { Children = { slider, label } });
            }

            var presets = Choice(EqualizerSettings.Presets.Select(p => p.Name), eq.CurrentPreset, i =>
            {
                eq.CurrentPreset = i;
                eq.Bypass = false;
                showing = true;
                for (var b = 0; b < bands.Length; b++)
                    bands[b].Value = eq.GetGain(b);
                showing = false;
            });
            return Stack(
                Section("Playback tools"),
                Row("Sleep timer", "Pauses playback after a while, with a short fade", sleep),
                Row("Volume levelling", "ReplayGain tags, or Pardeh's own loudness measurement for files without them",
                    Choice(["Off", "Even out each track", "Even out albums"], (int)player.ReplayGain, i =>
                    {
                        player.ReplayGain = (ReplayGainMode)i;
                        _settings.ReplayGain = player.ReplayGain.ToString();
                        _settings.Save();
                    })),
                Toggle("Gapless playback", "Join audio tracks of the same sample rate without a pause", player.Gapless, v =>
                {
                    player.Gapless = v;
                    _settings.Gapless = v;
                    _settings.Save();
                }),
                Toggle("Resume videos", "Videos of a minute or more carry on where they were left", player.ResumeVideos, v =>
                {
                    player.ResumeVideos = v;
                    _settings.ResumeVideos = v;
                    _settings.Save();
                }),
                Section("Network"),
                Row("Buffer", "Sound read ahead for streams and files on a server; more rides out a poor connection (used from the next address opened)",
                    Choice(NetworkOptions.BufferChoices.Select(s => $"{s} seconds"), Math.Max(0, Array.IndexOf(NetworkOptions.BufferChoices, _settings.NetworkBufferSeconds)), i =>
                    {
                        _settings.NetworkBufferSeconds = NetworkOptions.BufferChoices[i];
                        player.Network = _settings.ToNetworkOptions();
                        _settings.Save();
                    }, 140)),
                Row("Online video quality", "For streams that come in several sizes (HLS): the largest picture to take; a slow connection plays a smaller one more smoothly",
                    Choice(NetworkOptions.VideoHeightChoices.Select(h => $"Up to {h}p"), Math.Max(0, Array.IndexOf(NetworkOptions.VideoHeightChoices, _settings.NetworkVideoHeight)), i =>
                    {
                        _settings.NetworkVideoHeight = NetworkOptions.VideoHeightChoices[i];
                        player.Network = _settings.ToNetworkOptions();
                        _settings.Save();
                    }, 140)),
                Toggle("Reconnect after a drop", "Radio and files on a server are connected again, up to five times", _settings.NetworkReconnect, v =>
                {
                    _settings.NetworkReconnect = v;
                    player.Network = _settings.ToNetworkOptions();
                    _settings.Save();
                }),
                Section("Working on the playing track"),
                Note("A-B repeat, markers and subtitle timing work on the playing track: see Play > Playback Tools and Captions and Subtitles. Their keys are on the Keyboard page."),
                Section("Equalizer"),
                Toggle("Equalizer on", null, !eq.Bypass, v => eq.Bypass = !v),
                Row("Preset", "Sets the ten bands below (31 Hz to 16 kHz, -14 to +14 dB)", presets),
                bandRow);
        }

        private Control LyricsPage()
        {
            var lyrics = host?.Lyrics;
            var status = Note(string.Empty);
            var size = new NumericUpDown
            {
                Minimum = (decimal)(AppSettings.MinLyricsTextSize * 100), Maximum = (decimal)(AppSettings.MaxLyricsTextSize * 100), Increment = 10,
                Value = (decimal)Math.Round(_settings.LyricsTextSize * 100), FormatString = "0 '%'", Width = 140,
            };
            size.ValueChanged += (_, _) =>
            {
                _settings.LyricsTextSize = (double)(size.Value ?? 100) / 100;
                _settings.Save();
            };

            Control Setting(string title, string? description, bool value, Action<bool> set) =>
                Toggle(title, description, value, v => { set(v); _settings.Save(); });

            void Clear(Func<LyricsKit.LyricsController, string> action) => status.Text = lyrics is null ? "Nothing to clear." : action(lyrics);

            var deleteArmed = false;
            Button? delete = null;
            delete = Btn("Delete my edited lyrics", () =>
            {
                if (!deleteArmed)
                {
                    deleteArmed = true;
                    delete!.Content = "Click again to delete them all";
                    return;
                }

                deleteArmed = false;
                delete!.Content = "Delete my edited lyrics";
                Clear(l =>
                {
                    var count = l.Service.Store?.ClearOverrides() ?? 0;
                    l.Service.InvalidateAll();
                    l.Refresh();
                    return count == 1 ? "1 edited lyrics text deleted." : $"{count} edited lyrics texts deleted.";
                });
            });

            return Stack(
                Section("Lyrics and chords"),
                Note(Keymaps.WithKey("View > Lyrics and Chords shows them over the video area of a full mode", Pardeh.Core.Input.KeyCmd.Lyrics) + ", or puts them in their own window (the way to use them in skin mode)."),
                Setting("Show lyrics", "Look for the lyrics of the playing song", _settings.LyricsEnabled, v => _settings.LyricsEnabled = v),
                Setting("Follow while playing", "Keep the current line in view", _settings.LyricsFollow, v => _settings.LyricsFollow = v),
                Row("Text size", "Also the A buttons under the lyrics", size),
                Setting("Show chords", "Above the words, from a .cho / .crd file, [Am] marks or your own chords", _settings.LyricsShowChords, v => _settings.LyricsShowChords = v),
                Row("Chord spelling", "How transposed chords are written", Choice(["As written (auto)", "Sharps", "Flats"],
                    Enum.TryParse<ChordSpelling>(_settings.ChordSpelling, true, out var spelling) ? (int)spelling : 0,
                    i => { _settings.ChordSpelling = ((ChordSpelling)i).ToString(); _settings.Save(); })),
                Setting("Save fetched lyrics", "Keep lyrics found online on this computer, so they show again offline", _settings.LyricsSaveFetched, v => _settings.LyricsSaveFetched = v),
                Section("Online"),
                Setting("Look up lyrics online (LRCLIB)", MobileSettingsView.OnlinePrivacy, _settings.LyricsOnline, v => _settings.LyricsOnline = v),
                Note("Off by default. " + MobileSettingsView.OnlinePrivacy),
                LyricsSources(lyrics),
                Section("Stored lyrics"),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        Btn("Clear online lyrics cache", () => Clear(l =>
                        {
                            l.Service.Store?.ClearFetched();
                            l.Service.InvalidateAll();
                            l.Refresh();
                            return "Online lyrics cache cleared.";
                        })),
                        delete,
                    },
                },
                status);
        }

        /// <summary>The user's own lyrics addresses: on / off and remove for each, and two fields to add one.</summary>
        private Control LyricsSources(LyricsKit.LyricsController? lyrics)
        {
            var list = new StackPanel { Spacing = 4 };
            var message = Note(string.Empty);
            var name = new TextBox { PlaceholderText = "Name", Width = 150 };
            var address = new TextBox { PlaceholderText = "https://example.com/lyrics?artist={artist}&title={title}", Width = 330 };

            void Changed()
            {
                _settings.Save();
                lyrics?.Service.InvalidateAll();
                Fill();
            }

            void Add(string sourceName, string url)
            {
                url = url.Trim();
                if (!LyricsEndpoint.IsValid(url))
                {
                    message.Text = "The address must start with http:// or https:// and contain {title} or {artist}.";
                    return;
                }

                if (_settings.LyricsSources.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                {
                    message.Text = "That address is already in the list.";
                    return;
                }

                message.Text = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "Added. Note: http:// is not encrypted." : string.Empty;
                _settings.LyricsSources.Add(new LyricsEndpoint
                {
                    Name = sourceName.Trim().Length > 0 ? sourceName.Trim() : new Uri(url.Replace("{", string.Empty).Replace("}", string.Empty)).Host,
                    Url = url,
                });
                Changed();
            }

            void Fill()
            {
                list.Children.Clear();
                foreach (var source in _settings.LyricsSources.ToList())
                {
                    var enabled = new CheckBox { Content = source.Name, IsChecked = source.Enabled, Foreground = Modern ? Ink : null, MinWidth = 200 };
                    ToolTip.SetTip(enabled, source.Url);
                    enabled.IsCheckedChanged += (_, _) =>
                    {
                        source.Enabled = enabled.IsChecked == true;
                        _settings.Save();
                        lyrics?.Service.InvalidateAll();
                    };
                    list.Children.Add(new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { enabled, Btn("Remove", () => { _settings.LyricsSources.Remove(source); Changed(); }) },
                    });
                }

                var presets = new WrapPanel();
                foreach (var preset in LyricsEndpoint.Presets.Where(p => !_settings.LyricsSources.Any(s => s.Url == p.Url)))
                {
                    var button = Btn("Add " + preset.Name, () => Add(preset.Name, preset.Url));
                    button.Margin = new Thickness(0, 0, 8, 4);
                    presets.Children.Add(button);
                }

                if (presets.Children.Count > 0)
                    list.Children.Add(presets);
            }

            Fill();
            var fields = new WrapPanel { Children = { name, address, Btn("Add this source", () => Add(name.Text ?? string.Empty, address.Text ?? string.Empty)) } };
            name.Margin = address.Margin = new Thickness(0, 0, 8, 4);
            return Stack(Section("More lyrics sources"),
                Note("Any web address that returns lyrics as JSON or text. {title}, {artist}, {album} and {duration} are filled in; they are tried after LRCLIB, and only while online look-up is on."),
                list, fields, message);
        }
    }
}
