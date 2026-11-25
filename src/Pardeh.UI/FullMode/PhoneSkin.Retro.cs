using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Pardeh.UI.Rendering;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The "2001-2010 devices" phone looks: Windows Mobile 6, Windows Phone 7, Zune HD, Kin, the two Xbox dashboards and the
/// Sony Ericsson Walkman phones. Each one is a <see cref="RetroLook"/>: a palette for the shared phone generator plus its
/// own pictures (wallpaper, bars, navigation, transport, sliders) and a few edits of the shared layout and script
/// (title style, navigation labels, transparent queue). The other phone looks never pass through here.
/// All art is original, drawn in code; no logos or pictures of those products are used.
/// </summary>
internal static partial class PhoneSkin
{
    private sealed record RetroLook
    {
        public required Palette Palette { get; init; }

        /// <summary>Colours of the Settings and Library controls the app lays over the skin.</summary>
        public required MobileTheme Settings { get; init; }

        /// <summary>Replaces or adds pictures after the shared generator has drawn its own.</summary>
        public required Action<Dictionary<string, byte[]>, Palette> Art { get; init; }

        /// <summary>Page titles and navigation labels in lower case, as Zune, Kin and Windows Phone wrote them.</summary>
        public bool Lowercase { get; init; }

        /// <summary>The page title's point size, top and weight.</summary>
        public (int Size, int Top, bool Bold) Title { get; init; } = (12, 14, true);

        /// <summary>The title font when it differs from the look's own (the light faces of Zune and Windows Phone).</summary>
        public string? TitleFont { get; init; }

        /// <summary>Navigation labels: point size and top inside the bar. Null size: no labels (they are part of the pictures or not wanted).</summary>
        public (int? Size, int Top) NavText { get; init; } = (7, 38);

        /// <summary>Extra XML inside the Now Playing pane, behind its controls.</summary>
        public string NowDecor { get; init; } = string.Empty;

        /// <summary>Extra XML inside the top bar, and the script lines (view width in <c>w</c>) that place it.</summary>
        public string BarDecor { get; init; } = string.Empty;

        /// <summary>Script lines run after the Now Playing layout (<c>w</c>, <c>h</c>, <c>ch</c> are the view and content sizes).</summary>
        public string NowScript { get; init; } = string.Empty;
    }

    private static readonly Lazy<Dictionary<string, Func<RetroLook>>> RetroLooks = new(() => new(StringComparer.Ordinal)
    {
        ["wm6-phone"] = WindowsMobileLook,
        ["metro-phone"] = MetroLook,
        ["zune-phone"] = ZuneLook,
        ["kin-phone"] = KinLook,
        ["blades-phone"] = BladesLook,
        ["xbox-phone"] = XboxLook,
        ["walkman-phone"] = WalkmanLook,
    });

    private static RetroLook? Retro(string key) => RetroLooks.Value.TryGetValue(key, out var make) ? make() : null;

    /// <summary>The looks of this family: setting key and name, in the order the menus list them.</summary>
    internal static IEnumerable<(string Key, string Name)> RetroLookNames =>
        RetroLooks.Value.Select(l => (l.Key, l.Value().Palette.Name));

    /// <summary>The Settings / Library theme of a look of this family, or null for every other look.</summary>
    internal static MobileTheme? RetroSettingsTheme(string key) => Retro(key)?.Settings;

    // ---- Pictures -----------------------------------------------------------------------------

    private static void AddRetroArt(Dictionary<string, byte[]> files, Palette p)
    {
        if (Retro(p.Key) is not { } look)
            return;
        CoverFrame(files, p);
        look.Art(files, p);
    }

    /// <summary>
    /// The cover's frame in the look's own colours (the shared one is Luna blue). Its note shows until the cover or the classic
    /// default art is drawn over it (a queue restored paused has neither yet).
    /// </summary>
    private static void CoverFrame(Dictionary<string, byte[]> files, Palette p) => files["cover_frame.png"] = Png(76, 76, g =>
    {
        g.DrawRectangle(Vertical((p.NavTop, 0), (p.NavBottom, 1)), Pen(WithAlpha(p.Accent, 170)), new RoundedRect(new Rect(0.5, 0.5, 75, 75), p.Key == "wm6-phone" ? 4 : 0));
        FluentGlyph(g, Color.Parse(p.NavTop.R + p.NavTop.G + p.NavTop.B > 480 ? "#7A857D" : "#E8E8E8"), "music_note_2", false, 38, 38, 40);
    });

    /// <summary>The page background, stretched to the content area (behind Now Playing and the playlist).</summary>
    private static void Wallpaper(Dictionary<string, byte[]> files, Action<DrawingContext, double, double> draw)
    {
        const int Height = H - Bar - Nav;
        files["wall.png"] = Png(W, Height, g =>
        {
            using (g.PushClip(new Rect(0, 0, W, Height)))
                draw(g, W, Height);
        });
    }

    private static void BarArt(Dictionary<string, byte[]> files, Action<DrawingContext, Rect> draw) =>
        files["bar.png"] = Png(1, Bar, g => draw(g, new Rect(0, 0, 1, Bar)));

    private static void NavArt(Dictionary<string, byte[]> files, Action<DrawingContext, Rect> draw) =>
        files["nav.png"] = Png(1, Nav, g => draw(g, new Rect(0, 0, 1, Nav)));

    /// <summary>The five navigation pictures (72x52, the whole slot tappable); <paramref name="draw"/> gets the page key, its place and whether it is the open page.</summary>
    private static void NavButtons(Dictionary<string, byte[]> files, Action<DrawingContext, string, int, bool> draw)
    {
        for (var i = 0; i < NavIcons.Length; i++)
        {
            var (key, _) = NavIcons[i];
            var index = i;
            foreach (var selected in new[] { false, true })
            {
                files[selected ? $"nav_{key}_down.png" : $"nav_{key}.png"] = Png(72, 52, g =>
                {
                    g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 72, 52));
                    draw(g, key, index, selected);
                });
            }
        }
    }

    /// <summary>A glyph of the transport: Fluent or the generator's own 20 unit paths.</summary>
    private static void Glyph(DrawingContext g, bool fluent, string name, Color color, double cx, double cy, double size)
    {
        if (name == "stop")
        {
            var half = size * 0.27;
            g.DrawRectangle(Solid(color), null, new RoundedRect(new Rect(cx - half, cy - half, half * 2, half * 2), 1.5));
            return;
        }

        if (name is "back10" or "forward10")
        {
            if (fluent)
                FluentGlyph(g, color, name == "back10" ? "skip_back_10" : "skip_forward_10", false, cx, cy, size);
            else
                StrokeIcon(g, color, name == "back10" ? SkipBackPath : SkipForwardPath, cx, cy, size / 21.0, 1.55);
            return;
        }

        if (fluent)
        {
            FluentGlyph(g, color, name, true, name == "play" ? cx + 1 : cx, cy, size);
            return;
        }

        var path = name switch { "play" => PlayPath, "pause" => PausePath, "previous" => PrevPath, _ => NextPath };
        FillIcon(g, color, path, cx, cy, size / 17.0);
    }

    /// <summary>
    /// Play / pause (64), previous / next (52) and the 10 second skips (<see cref="SkipSize"/>). <paramref name="face"/> draws the
    /// button under the glyph (size, whether it is the big one, which glyph, state); <paramref name="ink"/> picks the glyph colour.
    /// </summary>
    private static void TransportArt(Dictionary<string, byte[]> files, bool fluent, Action<DrawingContext, double, bool, string, State> face,
        Func<bool, string, State, Color> ink, double heroGlyph = 30, double sideGlyph = 24, double skipGlyph = 26)
    {
        foreach (var (state, suffix) in States)
        {
            void Button(string file, string glyph, int size, bool hero, double glyphSize) => files[$"{file}{suffix}.png"] = Png(size, size, g =>
            {
                HitBox(g, size);
                face(g, size, hero, glyph, state);
                var color = ink(hero, glyph, state);
                Glyph(g, fluent, glyph, state == State.Disabled ? WithAlpha(color, 90) : color, size / 2.0, size / 2.0, glyphSize);
            });

            Button("play", "play", 64, true, heroGlyph);
            Button("pause", "pause", 64, true, heroGlyph);
            Button("prev", "previous", 52, false, sideGlyph);
            Button("next", "next", 52, false, sideGlyph);
            Button("stop", "stop", 52, false, sideGlyph); // used by the PC versions of these looks (no phone layout has a stop key)
            // The "10" arrows are outlines: on buttons with a face they need their own size.
            Button("skipb", "back10", SkipSize, false, skipGlyph);
            Button("skipf", "forward10", SkipSize, false, skipGlyph);
        }
    }

    /// <summary>Shuffle, repeat and mute (44): <paramref name="face"/> draws under the glyph when it is lit or not.</summary>
    private static void ToggleArt(Dictionary<string, byte[]> files, bool fluent, Color off, Color on, Action<DrawingContext, bool>? face = null)
    {
        foreach (var (name, path) in new[] { ("shuffle", IconShuffle), ("repeat", IconRepeat), ("mute", IconMute), ("muted", IconMuted) })
        {
            var icon = name switch { "shuffle" => "arrow_shuffle", "repeat" => "arrow_repeat_all", "mute" => "speaker_2", _ => "speaker_mute" };
            foreach (var down in new[] { false, true })
            {
                var lit = down || name == "muted";
                files[down ? $"{name}_down.png" : $"{name}.png"] = Png(44, 44, g =>
                {
                    HitBox(g, 44);
                    face?.Invoke(g, down);
                    var color = lit ? on : off;
                    if (fluent)
                        FluentGlyph(g, color, icon, down, 22, 22, 25);
                    else
                        StrokeIcon(g, color, path, 22, 22, 1.2, down ? 2.0 : 1.7);
                });
            }
        }
    }

    /// <summary>
    /// The seek and volume bars (6x30 tiles and a 30x30 thumb). <paramref name="track"/> and <paramref name="fill"/> paint a
    /// band <paramref name="height"/> units high in the middle of the touch strip.
    /// </summary>
    private static void SliderArt(Dictionary<string, byte[]> files, double height, Action<DrawingContext, Rect> track, Action<DrawingContext, Rect> fill, Action<DrawingContext, bool> thumb)
    {
        var band = new Rect(0, Math.Floor(15 - height / 2), 6, height);
        files["seek_track.png"] = Png(6, 30, g => track(g, band));
        files["seek_fill.png"] = Png(6, 30, g => fill(g, band));
        files["thumb.png"] = Png(30, 30, g => thumb(g, false));
        files["thumb_hover.png"] = Png(30, 30, g => thumb(g, true));
    }

    /// <summary>A glossy face of the 2000s: a vertical gradient, a lighter upper half and a rim.</summary>
    private static void Gel(DrawingContext g, Rect r, double radius, Color top, Color bottom, Color rim, byte gloss = 120, double rimWidth = 1)
    {
        var shape = new RoundedRect(r, radius);
        g.DrawRectangle(Vertical((top, 0), (bottom, 1)), Pen(rim, rimWidth), shape);
        if (gloss > 0)
            g.DrawRectangle(Vertical((WithAlpha(Colors.White, gloss), 0), (WithAlpha(Colors.White, (byte)(gloss / 4)), 1)), null,
                new RoundedRect(new Rect(r.X + 2, r.Y + 1.5, r.Width - 4, r.Height * 0.46), Math.Max(0, radius - 2)));
    }

    /// <summary>A soft round light that fades to nothing at its edge.</summary>
    private static void Glow(DrawingContext g, Point center, double rx, double ry, Color color)
    {
        var brush = new RadialGradientBrush
        {
            Center = RelativePoint.Center,
            GradientOrigin = RelativePoint.Center,
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(color, 0), new GradientStop(WithAlpha(color, (byte)(color.A * 0.45)), 0.55), new GradientStop(WithAlpha(color, 0), 1) },
        };
        g.DrawEllipse(brush, null, center, rx, ry);
    }

    private static Color Hx(string hex) => Color.Parse(hex);

    private static Geometry Path(FormattableString data) => Geo(FormattableString.Invariant(data));

    // ---- Layout and script ----------------------------------------------------------------------

    /// <summary>Sets (or adds) attributes on the element with this id. Throws when the shared layout no longer has it.</summary>
    private static string SetAttributes(string layout, string id, params (string Name, string Value)[] attributes)
    {
        var at = layout.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"The phone layout has no element '{id}'.");
        var start = layout.LastIndexOf('<', at);
        // The end of the opening tag: the first '>' outside a quoted value.
        var end = start;
        for (var quoted = false; end < layout.Length && (quoted || layout[end] != '>'); end++)
            if (layout[end] == '"')
                quoted = !quoted;
        var close = layout[end - 1] == '/' ? end - 1 : end;
        var tag = layout[start..close];
        foreach (var (name, value) in attributes)
        {
            var pattern = new Regex($"(?<=\\s){Regex.Escape(name)}=\"[^\"]*\"");
            var text = $"{name}=\"{value}\"";
            tag = pattern.IsMatch(tag) ? pattern.Replace(tag, _ => text, 1) : tag.TrimEnd() + " " + text;
        }

        return layout[..start] + tag + layout[close..];
    }

    private static string Insert(string text, string marker, string addition, bool after)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException($"The phone layout has no '{marker}'.");
        if (after)
            at = text.IndexOf('>', at) + 1;
        return text.Insert(at, addition);
    }

    private static string RetroLayout(Palette p, string layout)
    {
        if (Retro(p.Key) is not { } look)
            return layout;

        // One wallpaper behind every page; the playlist lets it through.
        layout = SetAttributes(layout, "svContent", ("backgroundImage", "wall.png"), ("resizeBackgroundImage", "true"));
        layout = SetAttributes(layout, "paneQueue", ("backgroundColor", "none"));
        layout = SetAttributes(layout, "ipl", ("transparentBackground", "true"));

        var (size, top, bold) = look.Title;
        layout = SetAttributes(layout, "tPage",
            ("top", top.ToString(CultureInfo.InvariantCulture)), ("height", Math.Min(Bar - top, size * 2).ToString(CultureInfo.InvariantCulture)),
            ("width", "250"), ("fontSize", size.ToString(CultureInfo.InvariantCulture)), ("fontStyle", bold ? "bold" : "normal"),
            ("fontType", look.TitleFont ?? p.Font));
        if (look.Lowercase)
            layout = SetAttributes(layout, "tPage", ("value", "now playing"));

        foreach (var id in new[] { "navNow", "navLibrary", "navQueue", "navRadio", "navSettings" })
        {
            var (navSize, navTop) = look.NavText;
            var name = id + "Label";
            layout = SetAttributes(layout, name, ("top", navTop.ToString(CultureInfo.InvariantCulture)), ("height", "18"),
                ("fontSize", (navSize ?? 7).ToString(CultureInfo.InvariantCulture)));
            if (navSize is null)
                layout = SetAttributes(layout, name, ("value", string.Empty));
            else if (look.Lowercase)
                layout = Regex.Replace(layout, $"(id=\"{name}\"[^>]*?value=\")([^\"]*)", m => m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant());
        }

        if (look.NowDecor.Length > 0)
            layout = Insert(layout, "<SUBVIEW id=\"svStage\"", look.NowDecor + "\n", after: false);
        if (look.BarDecor.Length > 0)
            layout = Insert(layout, "<TEXT id=\"tPage\"", look.BarDecor + "\n", after: false);
        return layout;
    }

    private static string RetroScript(Palette p, string script)
    {
        if (Retro(p.Key) is not { } look)
            return script;

        if (look.Lowercase)
        {
            const string Titles = "var TITLES = {";
            var from = script.IndexOf(Titles, StringComparison.Ordinal);
            var to = from < 0 ? -1 : script.IndexOf('}', from);
            if (to < 0)
                throw new InvalidOperationException("The phone script has no page titles.");
            script = script[..from] + script[from..to]
                .Replace("Now Playing", "now playing").Replace("Library", "library").Replace("Playlist", "playlist")
                .Replace("Radio", "radio").Replace("Settings", "settings") + script[to..];
        }

        if (look.NowScript.Length == 0)
            return script;
        const string Call = "LayoutNow(w, h, ch);"; // the call in Relayout (the definition has no semicolon)
        var call = script.LastIndexOf(Call, StringComparison.Ordinal);
        if (call < 0)
            throw new InvalidOperationException("The phone script no longer calls LayoutNow.");
        script = script.Insert(call + Call.Length, " RetroNow(w, h, ch);");
        return script + "\nfunction RetroNow(w, h, ch) {\n" + look.NowScript + "\n}\n";
    }
}
