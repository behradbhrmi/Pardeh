using Pardeh.Core.LyricsKit;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>What a LYRICS element shows.</summary>
public enum LyricsMode
{
    /// <summary>Lyrics are switched off in the settings.</summary>
    Off,

    /// <summary>Nothing is playing.</summary>
    NoTrack,

    /// <summary>Looking for the lyrics of the playing track.</summary>
    Loading,

    /// <summary>The track has none (offers search, paste and import).</summary>
    Empty,

    /// <summary>Timed lines: the current one is highlighted, a tap seeks.</summary>
    Synced,

    /// <summary>Plain text: scrolls, no highlight, no seek.</summary>
    Plain,

    /// <summary>The guitar tab, in monospaced text: scrolls like plain text.</summary>
    Tab,
}

/// <summary>The buttons (touch zones) a LYRICS element draws itself.</summary>
public enum LyricsZoneKind
{
    OffsetMinus,
    OffsetLabel,
    OffsetPlus,
    SizeMinus,
    SizePlus,
    More,
    Follow,
    Search,
    Paste,
    Import,
    EnableLyrics,

    // The chord bar above the toolbar, when the lyrics have chords.
    ChordDown,
    ChordKey,
    ChordUp,
    ChordSpelling,
    ChordToggle,

    /// <summary>Between the words and the guitar tab (in the chord bar).</summary>
    TabToggle,
}

/// <summary>A touch zone in the element's own coordinates.</summary>
public readonly record struct LyricsZone(LyricsZoneKind Kind, double X, double Y, double W, double H)
{
    public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>Scroll and layout maths of the lyrics view, free of any UI so that it can be tested alone.</summary>
public static class LyricsScroll
{
    /// <summary>The scroll position that centres a line (top, height) in a viewport, kept inside the content.</summary>
    public static double CenterOn(double lineTop, double lineHeight, double viewport, double content) =>
        Math.Clamp(lineTop + lineHeight / 2 - viewport / 2, 0, Math.Max(0, content - viewport));

    /// <summary>One step of an exponential ease towards the target; <paramref name="ms"/> is the time since the last step. About 95 % of the way after <paramref name="easeMs"/>.</summary>
    public static double Ease(double current, double target, double ms, double easeMs)
    {
        if (ms <= 0)
            return current;
        var next = current + (target - current) * (1 - Math.Exp(-3 * ms / Math.Max(1, easeMs)));
        return Math.Abs(target - next) < 0.25 ? target : next;
    }

    /// <summary>The index of the line that contains <paramref name="y"/> (content coordinates), or -1. Tops ascend.</summary>
    public static int LineAt(IReadOnlyList<double> tops, IReadOnlyList<double> heights, double y)
    {
        int lo = 0, hi = tops.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (tops[mid] <= y)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found >= 0 && y < tops[found] + heights[found] ? found : -1;
    }

    /// <summary>The first line that reaches below <paramref name="y"/> (content coordinates); the count when none does.</summary>
    public static int FirstVisible(IReadOnlyList<double> tops, IReadOnlyList<double> heights, double y)
    {
        int lo = 0, hi = tops.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (tops[mid] + heights[mid] <= y)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    /// <summary>Opacity 0..1 of something at <paramref name="y"/> in a viewport of <paramref name="height"/>: it fades over <paramref name="fade"/> at both edges.</summary>
    public static double EdgeFade(double y, double height, double fade)
    {
        if (fade <= 0)
            return 1;
        var d = Math.Min(y, height - y);
        var t = Math.Clamp(d / fade, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// How far a line is filled when it has word timings: the character position (0..<paramref name="textLength"/>, fractional) reached at
    /// <paramref name="position"/>. Words fill one after the other; inside a word the fill moves with time. The last word ends at
    /// <paramref name="lineEnd"/>.
    /// </summary>
    public static double FilledChars(IReadOnlyList<LyricWord> words, TimeSpan lineEnd, TimeSpan position, int textLength)
    {
        if (words.Count == 0 || textLength <= 0)
            return 0;
        var total = 0;
        foreach (var word in words)
            total += word.Text.Length;
        if (total <= 0)
            return 0;
        double chars = 0;
        for (var i = 0; i < words.Count; i++)
        {
            var start = words[i].Time;
            var end = i + 1 < words.Count ? words[i + 1].Time : lineEnd;
            var length = words[i].Text.Length;
            if (position >= end)
            {
                chars += length;
                continue;
            }

            if (position > start && end > start)
                chars += length * (position - start).TotalSeconds / (end - start).TotalSeconds;
            break;
        }

        return Math.Clamp(chars / total * textLength, 0, textLength);
    }
}

/// <summary>
/// LYRICS (phone looks): the lyrics of the playing track as a scrolling view that follows the song (<see cref="ILyricsFeed"/> from the
/// session). Synchronised lyrics highlight the current line, centre it with an ease, seek on a tap and pause following for a few seconds
/// after the user scrolls; plain lyrics just scroll. Loading and empty states, a compact toolbar (offset, text size, menu) and the
/// actions are part of the element; the renderer draws them. The text layout (line heights) is measured by the renderer
/// (<see cref="SetLayout"/>); everything else here is plain maths.
/// </summary>
public sealed class LyricsElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent), IPlayerSynced, IPixelScrollable
{
    /// <summary>How long following stays off after a manual scroll (ms).</summary>
    public const double FollowPauseMs = 4000;

    /// <summary>The ease of the scroll and of the highlight growing (ms).</summary>
    public const double EaseMs = 250;

    /// <summary>The current line is drawn this much larger (the layout leaves room for it).</summary>
    public const double HighlightScale = 1.12;

    /// <summary>Space left and right of the text (units).</summary>
    public const double SidePad = 14;

    /// <summary>The offset buttons move the timing by this much (ms).</summary>
    public const int OffsetStepMs = 500;

    /// <summary>The text size buttons move the size factor by this much.</summary>
    public const double SizeStep = 0.1;

    /// <summary>The time source in milliseconds (tests replace it).</summary>
    public Func<long> Clock { get; set; } = () => Environment.TickCount64;

    protected override IEnumerable<string> KnownProperties =>
        ["toolbarHeight", "lyricsSize", "textColor", "accentColor", "icons", "plateColor", "backColor", "align", "fontType"];

    public override bool IsInteractive => true;

    public override bool HitTest(double x, double y) => Visible && base.HitTest(x, y);

    // ---- Style (attributes) ----------------------------------------------------------------------------

    /// <summary>Height of the toolbar under the text (0 hides it).</summary>
    public double ToolbarHeight => Math.Max(0, GetDouble("toolbarHeight", 40));

    /// <summary>Text size of a normal line in units, before the user's size factor.</summary>
    public double BaseFontSize => Math.Max(8, GetDouble("lyricsSize", 16));

    public string FontFamily => GetString("fontType") ?? "Segoe UI";

    /// <summary>An opaque background (RGB) painted under the lyrics, or null for none (the stage's own colour hides the frame art under it).</summary>
    public int? BackColor => SkinValue.ParseColor(Get("backColor"));

    public int InkColor => SkinValue.ParseColor(Get("textColor")) ?? 0xFFFFFF;

    public int AccentColor => SkinValue.ParseColor(Get("accentColor")) ?? InkColor;

    /// <summary>A pale plate behind buttons and the follow pill (RGB; the renderer adds the alpha).</summary>
    public int PlateColor => SkinValue.ParseColor(Get("plateColor")) ?? 0xFFFFFF;

    /// <summary>"fluent" (the modern looks) or "pictorial" (the vintage looks): the icon family of the toolbar.</summary>
    public bool FluentIcons => !string.Equals(GetString("icons"), "pictorial", StringComparison.OrdinalIgnoreCase);

    /// <summary>"center" (default) or "start": where lines sit; start-aligned right-to-left lines sit at the right.</summary>
    public bool CenterLines => !string.Equals(GetString("align"), "start", StringComparison.OrdinalIgnoreCase);

    // ---- State -------------------------------------------------------------------------------------------

    public ILyricsFeed? Feed => View.Session.Lyrics;

    public LyricsMode Mode
    {
        get
        {
            if (Feed is not { } feed || !feed.LyricsEnabled)
                return LyricsMode.Off;
            if (feed.Current is { } lyrics)
                return lyrics.Tab is not null && (feed.ShowTab || !lyrics.HasText) ? LyricsMode.Tab : lyrics.Synced ? LyricsMode.Synced : LyricsMode.Plain;
            if (feed.IsLoading)
                return LyricsMode.Loading;
            return feed.HasTrack ? LyricsMode.Empty : LyricsMode.NoTrack;
        }
    }

    public bool HasToolbar => ToolbarHeight > 0 && Mode is LyricsMode.Synced or LyricsMode.Plain or LyricsMode.Tab;

    public const double ChordBarHeightUnits = 36;

    /// <summary>
    /// The chord bar (transpose, spelling, show / hide) sits above the toolbar when the lyrics have chords; it also holds the
    /// switch between the words and the tab. (A track with only a tab has nothing to switch to.)
    /// </summary>
    public bool HasChordBar => HasToolbar && Feed?.Current is { } shown && (Mode == LyricsMode.Tab ? shown.HasText : shown.HasChords || shown.Tab is not null);

    public double ChordBarHeight => HasChordBar ? ChordBarHeightUnits : 0;

    /// <summary>True when chords are drawn above the words.</summary>
    public bool ShowsChords => Feed is { ShowChords: true } feed && feed.Current is { HasChords: true };

    /// <summary>What besides lyrics, width and size changes the measured lines.</summary>
    private (bool, int, ChordSpelling) ChordVariant =>
        ShowsChords ? (true, Feed!.Current!.Transpose, Feed.ChordSpelling) : (false, 0, ChordSpelling.Auto);

    /// <summary>The height of the text area (the element without the toolbar and the chord bar).</summary>
    public double TextHeight => Math.Max(0, Height - (HasToolbar ? ToolbarHeight : 0) - ChordBarHeight);

    /// <summary>Whether this element and all its parents are visible (nothing is done for a hidden lyrics view).</summary>
    public bool IsShown
    {
        get
        {
            for (SkinElement? e = this; e is not null; e = e.Parent)
                if (!e.Visible)
                    return false;
            return Width > 0 && Height > 0;
        }
    }

    // ---- Layout (measured by the renderer) ------------------------------------------------------------------

    private double[] _heights = [];
    private double[] _tops = [];
    private double _contentHeight;
    private double _padFor = double.NaN;
    private Lyrics? _layoutLyrics;
    private double _layoutWidth, _layoutSize;

    /// <summary>The width the renderer wraps lines to (the highlighted line grows into the rest).</summary>
    public double WrapWidth => Math.Max(20, (Width - 2 * SidePad) / HighlightScale);

    /// <summary>The line font size in units with the user's size factor.</summary>
    public double LineFontSize => BaseFontSize * (Feed?.TextSize ?? 1);

    public IReadOnlyList<double> Tops
    {
        get
        {
            EnsureTops();
            return _tops;
        }
    }

    public IReadOnlyList<double> Heights => _heights;

    public double ContentHeight
    {
        get
        {
            if (Mode == LyricsMode.Tab)
                return _tabHeight;
            EnsureTops();
            return _contentHeight;
        }
    }

    private Lyrics? _tabLyrics;
    private (double Width, double Size) _tabKey;
    private double _tabHeight;

    /// <summary>True when the renderer has to wrap the tab again (another track, width or text size).</summary>
    public bool NeedsTabLayout(Lyrics lyrics) =>
        !ReferenceEquals(lyrics, _tabLyrics) || Math.Abs(Width - _tabKey.Width) > 0.5 || Math.Abs(LineFontSize - _tabKey.Size) > 0.01;

    /// <summary>The height of the wrapped tab with its margins. A new tab starts at the top.</summary>
    public void SetTabLayout(Lyrics lyrics, double height)
    {
        var fresh = !ReferenceEquals(lyrics, _tabLyrics);
        _tabLyrics = lyrics;
        _tabKey = (Width, LineFontSize);
        _tabHeight = height;
        ScrollY = fresh ? 0 : Math.Clamp(ScrollY, 0, MaxScroll);
        _target = ScrollY;
    }

    public double MaxScroll => Math.Max(0, ContentHeight - TextHeight);

    /// <summary>True when the renderer has to measure the lines again (other lyrics, width or text size).</summary>
    public bool NeedsLayout(Lyrics lyrics) =>
        !ReferenceEquals(lyrics, _layoutLyrics) || Math.Abs(WrapWidth - _layoutWidth) > 0.5 || Math.Abs(LineFontSize - _layoutSize) > 0.01 || _heights.Length != lyrics.Lines.Count
        || ChordVariant != _layoutChords;

    private (bool, int, ChordSpelling) _layoutChords;

    /// <summary>Stores the measured height of every line (including its spacing). Scrolls to the current line without easing for new lyrics.</summary>
    public void SetLayout(Lyrics lyrics, double[] heights)
    {
        var fresh = !ReferenceEquals(lyrics, _layoutLyrics);
        _layoutLyrics = lyrics;
        _layoutWidth = WrapWidth;
        _layoutSize = LineFontSize;
        _layoutChords = ChordVariant;
        _heights = heights;
        _padFor = double.NaN;
        EnsureTops();
        ScrollY = Math.Clamp(ScrollY, 0, MaxScroll);
        if (fresh)
        {
            _detachedUntil = 0;
            var at = Feed?.CurrentIndex ?? -1;
            ScrollY = at >= 0 && Mode == LyricsMode.Synced && (Feed?.Follow ?? true) ? TargetFor(at) : 0;
            _target = ScrollY;
        }
        else if (_index >= 0 && Mode == LyricsMode.Synced && !IsDetached && (Feed?.Follow ?? true))
        {
            // The text size or the width changed: the current line stays in the middle.
            ScrollY = _target = TargetFor(_index);
        }
    }

    /// <summary>Space above the first and below the last line: half the viewport for timed lyrics (any line can be centred), a little for plain text.</summary>
    private double Pad => Mode == LyricsMode.Synced ? TextHeight * 0.42 : 14;

    private void EnsureTops()
    {
        var pad = Pad;
        if (_padFor == pad && _tops.Length == _heights.Length)
            return;
        _padFor = pad;
        if (_tops.Length != _heights.Length)
            _tops = new double[_heights.Length];
        var y = pad;
        for (var i = 0; i < _heights.Length; i++)
        {
            _tops[i] = y;
            y += _heights[i];
        }

        _contentHeight = y + (Mode == LyricsMode.Synced ? pad : 24);
    }

    // ---- Scrolling and following --------------------------------------------------------------------------------

    public double ScrollY { get; private set; }

    private double _target;
    private long _detachedUntil;
    private long _lastSync;
    private int _index = -1;
    private int _previousIndex = -1;
    private double _emphasis = 1;

    /// <summary>The scroll position that centres line <paramref name="index"/>.</summary>
    public double TargetFor(int index) =>
        index >= 0 && index < _heights.Length ? LyricsScroll.CenterOn(Tops[index], _heights[index], TextHeight, ContentHeight) : 0;

    /// <summary>The line that is current for the highlight (-1 for plain lyrics).</summary>
    public int CurrentLine => _index;

    public int PreviousLine => _previousIndex;

    /// <summary>1 for the current line, 0 for the others; moves over <see cref="EaseMs"/> when the line changes.</summary>
    public double Emphasis(int line) => line == _index ? _emphasis : line == _previousIndex ? 1 - _emphasis : 0;

    /// <summary>True while the user's scrolling keeps the view from following.</summary>
    public bool IsDetached => Clock() < _detachedUntil;

    /// <summary>The "follow" pill: shown when the view is away from the current line and does not return by itself.</summary>
    public bool ShowFollowButton =>
        Mode == LyricsMode.Synced && _index >= 0 && _index < _heights.Length && Math.Abs(ScrollY - TargetFor(_index)) > TextHeight * 0.22
        && (IsDetached || Feed?.Follow == false);

    /// <summary>Scrolls by pixels (a touch drag or a fling): the view stops following for <see cref="FollowPauseMs"/>.</summary>
    public void ScrollByPixels(double dy)
    {
        if (Mode is not (LyricsMode.Synced or LyricsMode.Plain or LyricsMode.Tab))
            return;
        ScrollY = Math.Clamp(ScrollY + dy, 0, MaxScroll);
        _target = ScrollY;
        if (Mode == LyricsMode.Synced)
            _detachedUntil = Clock() + (long)FollowPauseMs;
        View.Invalidate();
    }

    public override void OnWheel(double delta) => ScrollByPixels(-delta * 60);

    /// <summary>Goes back to the current line and follows again.</summary>
    public void Resume()
    {
        _detachedUntil = 0;
        if (_index >= 0)
            _target = TargetFor(_index);
        View.Invalidate();
    }

    /// <summary>Per tick: follows the playing line, eases the scroll and the highlight. Does nothing while the element is hidden.</summary>
    public void SyncWithPlayer()
    {
        if (!IsShown)
        {
            _lastSync = 0;
            return;
        }

        var now = Clock();
        var dt = _lastSync == 0 ? 16 : Math.Clamp(now - _lastSync, 0, 100);
        _lastSync = now;
        if (Feed is not { } feed || Mode != LyricsMode.Synced)
        {
            _index = -1;
            _previousIndex = -1;
            return;
        }

        var index = feed.CurrentIndex;
        if (index != _index)
        {
            _previousIndex = _index;
            _index = index;
            _emphasis = 0;
        }

        if (_emphasis < 1)
            _emphasis = Math.Min(1, _emphasis + dt / EaseMs);

        if (_heights.Length == 0)
            return;
        if (_detachedUntil != 0 && now >= _detachedUntil)
            _detachedUntil = 0;
        if (_detachedUntil == 0 && feed.Follow && _index >= 0)
        {
            _target = TargetFor(_index);
            ScrollY = LyricsScroll.Ease(ScrollY, _target, dt, EaseMs);
        }
    }

    // ---- Zones (buttons) -----------------------------------------------------------------------------------------

    private LyricsZone[] _zones = [];
    private (double W, double H, LyricsMode Mode, bool Online, bool Follow, bool Bar, bool Chords, string? Tab) _zonesKey;

    /// <summary>The buttons of the current state: toolbar, follow pill, or the actions of the empty state.</summary>
    public IReadOnlyList<LyricsZone> Zones
    {
        get
        {
            var mode = Mode;
            var key = (Width, Height, mode, Feed?.OnlineEnabled ?? false, ShowFollowButton, HasChordBar, ShowsChords, Feed?.Current?.Tab);
            if (key != _zonesKey)
            {
                _zonesKey = key;
                _zones = BuildZones(mode);
            }

            return _zones;
        }
    }

    /// <summary>Where the title of the empty / off state is drawn (units from the top).</summary>
    public double MessageTop { get; private set; }

    /// <summary>Height reserved for the title and its explanation above the empty state's buttons.</summary>
    public const double MessageHeight = 78;

    private LyricsZone[] BuildZones(LyricsMode mode)
    {
        var zones = new List<LyricsZone>();
        switch (mode)
        {
            case LyricsMode.Synced or LyricsMode.Plain or LyricsMode.Tab:
                if (HasToolbar)
                {
                    var synced = mode == LyricsMode.Synced;
                    var n = synced ? 5 : 3;
                    var label = synced ? 56.0 : 0;
                    var bw = Math.Clamp((Width - 12 - label) / n, 30, 46);
                    var x = (Width - (n * bw + label)) / 2;
                    var y = Height - ToolbarHeight;
                    var h = ToolbarHeight;
                    if (synced)
                    {
                        zones.Add(new(LyricsZoneKind.OffsetMinus, x, y, bw, h)); x += bw;
                        zones.Add(new(LyricsZoneKind.OffsetLabel, x, y, label, h)); x += label;
                        zones.Add(new(LyricsZoneKind.OffsetPlus, x, y, bw, h)); x += bw;
                    }

                    zones.Add(new(LyricsZoneKind.SizeMinus, x, y, bw, h)); x += bw;
                    zones.Add(new(LyricsZoneKind.SizePlus, x, y, bw, h)); x += bw;
                    zones.Add(new(LyricsZoneKind.More, x, y, bw, h));
                }

                if (HasChordBar)
                {
                    var y = Height - ToolbarHeight - ChordBarHeight;
                    // Shown: - key + , spelling, hide. Hidden: just "Show chords".
                    var withTab = Feed?.Current?.Tab is not null;
                    (LyricsZoneKind Kind, double W)[] cells = mode == LyricsMode.Tab
                        ? [(LyricsZoneKind.TabToggle, 132)]
                        : Feed?.Current is not { HasChords: true }
                            ? [(LyricsZoneKind.TabToggle, 132)]
                            : ShowsChords
                                ? [(LyricsZoneKind.ChordDown, 40), (LyricsZoneKind.ChordKey, 70), (LyricsZoneKind.ChordUp, 40), (LyricsZoneKind.ChordSpelling, 76), (LyricsZoneKind.ChordToggle, 56)]
                                : [(LyricsZoneKind.ChordToggle, 132)];
                    if (withTab && cells is [.., { Kind: not LyricsZoneKind.TabToggle }])
                        cells = [.. cells, (LyricsZoneKind.TabToggle, 56)];
                    var scale = Math.Min(1, (Width - 8) / cells.Sum(c => c.W));
                    var x = (Width - cells.Sum(c => c.W) * scale) / 2;
                    foreach (var (kind, w) in cells)
                    {
                        zones.Add(new(kind, x, y, w * scale, ChordBarHeight));
                        x += w * scale;
                    }
                }

                if (mode == LyricsMode.Synced && ShowFollowButton)
                    zones.Add(new(LyricsZoneKind.Follow, Width - 12 - 92, TextHeight - 36 - 6, 92, 36));
                break;
            case LyricsMode.Empty or LyricsMode.Off:
                var kinds = mode == LyricsMode.Off
                    ? new[] { LyricsZoneKind.EnableLyrics }
                    : new[] { LyricsZoneKind.Search, LyricsZoneKind.Paste, LyricsZoneKind.Import };
                const double bh = 38, gap = 8;
                var total = MessageHeight + kinds.Length * bh + (kinds.Length - 1) * gap;
                var top = Math.Max(2, (Height - total) / 2);
                MessageTop = top;
                var pw = Math.Min(Width - 32, 236);
                var py = top + MessageHeight;
                foreach (var kind in kinds)
                {
                    zones.Add(new(kind, (Width - pw) / 2, py, pw, bh));
                    py += bh + gap;
                }

                break;
            default:
                MessageTop = Math.Max(2, (Height - MessageHeight) / 2);
                break;
        }

        return [.. zones];
    }

    public LyricsZone? ZoneAt(double x, double y)
    {
        foreach (var zone in Zones)
            if (zone.Contains(x, y))
                return zone;
        return null;
    }

    /// <summary>The label of a zone (the renderer draws it; tests read it).</summary>
    public string LabelOf(LyricsZoneKind kind) => kind switch
    {
        LyricsZoneKind.Search => Feed?.OnlineEnabled == true ? "Search online" : "Search online...",
        LyricsZoneKind.Paste => "Paste lyrics",
        LyricsZoneKind.Import => "Import .lrc file",
        LyricsZoneKind.EnableLyrics => "Turn lyrics on",
        LyricsZoneKind.Follow => "Follow",
        LyricsZoneKind.OffsetLabel => OffsetText,
        LyricsZoneKind.ChordDown => "-",
        LyricsZoneKind.ChordUp => "+",
        LyricsZoneKind.ChordKey => Feed?.Current is { Transpose: not 0 and var by } ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Key {(by > 0 ? "+" : "")}{by}") : "Key 0",
        LyricsZoneKind.ChordSpelling => Feed?.ChordSpelling switch { ChordSpelling.Sharps => "♯ Sharp", ChordSpelling.Flats => "♭ Flat", _ => "♯♭ Auto" },
        LyricsZoneKind.ChordToggle => ShowsChords ? "Hide" : "Show chords",
        LyricsZoneKind.TabToggle => Mode == LyricsMode.Tab ? "Show lyrics" : Feed?.Current is { HasChords: true } ? "Tab" : "Show tab",
        _ => string.Empty,
    };

    /// <summary>The timing correction as text: "+0.5 s", "-1.0 s", "0 s".</summary>
    public string OffsetText
    {
        get
        {
            var ms = Feed?.Current?.OffsetMs ?? 0;
            return ms == 0 ? "0 s" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(ms > 0 ? "+" : "-")}{Math.Abs(ms) / 1000.0:0.0} s");
        }
    }

    /// <summary>A short note on where the lyrics came from ("Embedded", ".lrc file", "Online", "Edited").</summary>
    public string SourceText => Feed?.Current is { } lyrics
        ? lyrics.ChordSource is { } chords ? SourceName(lyrics.Source) + " + " + (chords == LyricsSources.OwnChords ? "my chords" : "chords file") : SourceName(lyrics.Source)
        : string.Empty;

    public static string SourceName(string? source) => source switch
    {
        LyricsSources.Override => "Edited",
        LyricsSources.Embedded => "Embedded",
        LyricsSources.Sidecar => ".lrc file",
        LyricsSources.Online or LyricsSources.OnlineCache => "Online",
        LyricsSources.ChordFile => "Chords file",
        LyricsSources.OwnChords => "My chords",
        LyricsSources.TabFile => "Tab",
        _ => string.Empty,
    };

    /// <summary>The line at <paramref name="y"/> (element coordinates, text area) with the current scroll, or -1.</summary>
    public int LineAtView(double y) => y < 0 || y >= TextHeight ? -1 : LyricsScroll.LineAt(Tops, _heights, y + ScrollY);

    // ---- Input -------------------------------------------------------------------------------------------------

    private double _pressX, _pressY;

    /// <summary>The button under the finger right now (drawn pressed).</summary>
    public LyricsZoneKind? PressedKind { get; private set; }

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        _pressX = x;
        _pressY = y;
        PressedKind = ZoneAt(x, y)?.Kind;
    }

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        PressedKind = null;
        base.OnPointerReleased(x, y, inside);
        if (inside && Enabled && Math.Abs(y - _pressY) < 12 && Math.Abs(x - _pressX) < 12)
            Tap(x, y);
    }

    /// <summary>A tap at (x, y): a button, or a line of timed lyrics (seeks there).</summary>
    public void Tap(double x, double y)
    {
        if (Feed is not { } feed)
            return;
        if (ZoneAt(x, y) is { } zone)
        {
            Activate(zone.Kind);
            return;
        }

        if (Mode != LyricsMode.Synced)
            return;
        var line = LineAtView(y);
        if (line >= 0 && feed.Current is { } lyrics && !lyrics.Lines[line].IsBlank && feed.SeekToLine(line))
        {
            _detachedUntil = 0;
            _target = TargetFor(line);
        }

        View.Invalidate();
    }

    public void Activate(LyricsZoneKind kind)
    {
        var feed = Feed;
        switch (kind)
        {
            case LyricsZoneKind.OffsetMinus: feed?.NudgeOffset(-OffsetStepMs); break;
            case LyricsZoneKind.OffsetPlus: feed?.NudgeOffset(OffsetStepMs); break;
            case LyricsZoneKind.OffsetLabel: feed?.SetOffset(0); break;
            case LyricsZoneKind.SizeMinus: feed?.ChangeTextSize(-SizeStep); break;
            case LyricsZoneKind.SizePlus: feed?.ChangeTextSize(SizeStep); break;
            case LyricsZoneKind.Follow: Resume(); break;
            case LyricsZoneKind.More: ShowMenu(); break;
            case LyricsZoneKind.Search: Command("search"); break;
            case LyricsZoneKind.Paste: Command("paste"); break;
            case LyricsZoneKind.Import: Command("import"); break;
            case LyricsZoneKind.EnableLyrics: Command("enable"); break;
            case LyricsZoneKind.ChordDown: feed?.NudgeTranspose(-1); break;
            case LyricsZoneKind.ChordUp: feed?.NudgeTranspose(1); break;
            case LyricsZoneKind.ChordKey: feed?.SetTranspose(0); break;
            case LyricsZoneKind.ChordSpelling: feed?.CycleChordSpelling(); break;
            case LyricsZoneKind.ChordToggle: feed?.ToggleShowChords(); break;
            case LyricsZoneKind.TabToggle:
                // Both views are measured again and the words follow the song from the right place.
                _layoutLyrics = null;
                _tabLyrics = null;
                ScrollY = _target = 0;
                feed?.ToggleShowTab();
                break;
        }

        View.Invalidate();
    }

    private void Command(string action) => View.Session.Host.RunCommand("lyrics", [action]);

    /// <summary>The "more" menu: edit, import, look up, reset timing, delete my lyrics, refresh.</summary>
    public IReadOnlyList<(string Text, string Action)> MenuItems()
    {
        var feed = Feed;
        var items = new List<(string, string)>
        {
            (feed?.Current is null ? "Paste lyrics..." : "Edit lyrics...", "paste"),
            ("Import a .lrc file...", "import"),
            (feed?.OnlineEnabled == true ? "Search online again" : "Search online...", "search"),
        };
        var ownChords = feed?.Current is { } shown && (shown.Source == LyricsSources.OwnChords || shown.ChordSource == LyricsSources.OwnChords);
        items.Add((ownChords ? "Edit my chords..." : "Add my chords...", "chords"));
        if (ownChords)
            items.Add(("Delete my chords", "deletechords"));
        if (feed?.Current is { OffsetMs: not 0 })
            items.Add(("Reset timing", "resettiming"));
        if (feed?.Current is { Source: LyricsSources.Override })
            items.Add(("Delete my lyrics", "delete"));
        items.Add(("Refresh", "refresh"));
        return items;
    }

    private void ShowMenu()
    {
        var items = MenuItems();
        View.Session.Host.ShowPopup(View, [.. items.Select(i => i.Text)], -1, choice =>
        {
            if (choice < 0 || choice >= items.Count)
                return;
            if (items[choice].Action == "resettiming")
                Feed?.SetOffset(0);
            else
                Command(items[choice].Action);
            View.Invalidate();
        });
    }

    public override bool OnContextMenu(double x, double y)
    {
        if (Mode is LyricsMode.Synced or LyricsMode.Plain or LyricsMode.Tab or LyricsMode.Empty)
        {
            ShowMenu();
            return true;
        }

        return false;
    }
}
