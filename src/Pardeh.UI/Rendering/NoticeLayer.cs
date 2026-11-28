using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Pardeh.Core.Notices;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>Where the on-screen message sits over the picture.</summary>
internal enum NoticePlace
{
    Top,
    TopLeft,
    TopRight,
    Centre,
    Bottom,
}

/// <summary>How and where the message is drawn this frame.</summary>
/// <param name="Area">The picture (or the whole skin), in the layer's own units.</param>
/// <param name="Scale">Larger on a large full screen.</param>
/// <param name="TopInset">Room kept free at the top (a skin's title bar).</param>
/// <param name="BottomInset">Room kept free at the bottom (the full-screen control bar).</param>
internal readonly record struct NoticeFrame(Rect Area, NoticePlace Place, Color Accent, double Scale = 1, double TopInset = 0, double BottomInset = 0);

/// <summary>
/// Draws the on-screen message over every look: a dark card that reads on any picture. It takes no clicks and no focus.
/// </summary>
internal sealed class NoticeLayer : Control
{
    private Notice? _notice;
    private double _opacity;
    private NoticeFrame _frame;
    private string _drawn = string.Empty;

    public NoticeLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>Takes what to show; draws again only when something that shows has changed.</summary>
    public void Update(Notice? notice, double opacity, NoticeFrame frame)
    {
        // The running time is drawn in whole seconds and the bar in pixels: smaller changes are not worth a frame.
        var now = notice is null ? string.Empty : string.Create(CultureInfo.InvariantCulture,
            $"{notice.Key}|{notice.Title}|{notice.Value}|{notice.Detail}|{notice.Level:0.000}|{TimeText(notice.Timeline)}|{notice.Timeline?.Fraction:0.000}|{notice.Timeline?.LoopStart}|{notice.Timeline?.LoopEnd}|{opacity:0.00}|{frame}");
        _notice = notice;
        _opacity = opacity;
        _frame = frame;
        if (now == _drawn)
            return;
        _drawn = now;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_notice is not null && _opacity > 0.01)
            Draw(context, _notice, _opacity, _frame);
    }

    private static string TimeText(NoticeTimeline? timeline) => timeline is null ? string.Empty
        : MediaPlayer.FormatTime(timeline.Position) + "/" + MediaPlayer.FormatTime(timeline.Duration);

    // ---- Drawing ------------------------------------------------------------------------------------------

    private const double Pad = 12, IconSize = 20, Gap = 10, BarHeight = 4;
    private const double TitleSize = 14, DetailSize = 12, TimeSize = 12;

    // Immutable: they are shared, and may be drawn with from whichever thread draws.
    private static readonly IBrush Surface = new ImmutableSolidColorBrush(Color.FromArgb(222, 18, 20, 26));
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(56, 255, 255, 255)));
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush SubInk = new ImmutableSolidColorBrush(Color.FromRgb(196, 200, 210));
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    private static readonly Color Warning = Color.FromRgb(255, 183, 77);

    /// <summary>The card's rectangle (before <see cref="NoticeFrame.Scale"/>) for a message in this frame.</summary>
    internal static Rect Layout(Notice notice, NoticeFrame frame) => new Card(notice, frame).Bounds;

    public static void Draw(DrawingContext context, Notice notice, double opacity, NoticeFrame frame)
    {
        var card = new Card(notice, frame);
        var bounds = card.Bounds;
        using var scale = context.PushTransform(Matrix.CreateScale(frame.Scale, frame.Scale));
        using var fade = context.PushOpacity(Math.Clamp(opacity, 0, 1));
        using var antialias = context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias });

        var radius = Math.Min(8, bounds.Height / 2);
        context.DrawRectangle(Surface, Edge, bounds, radius, radius);
        var accent = new ImmutableSolidColorBrush(notice.Category == NoticeCategory.Problem ? Warning : frame.Accent);

        var inner = bounds.Deflate(card.Padding);
        var rowHeight = Math.Max(card.IconSide, card.Title.Height + (card.Detail?.Height ?? 0));
        DrawIcon(context, notice.Icon, new Rect(inner.X, inner.Y + (rowHeight - card.IconSide) / 2, card.IconSide, card.IconSide),
            notice.Category == NoticeCategory.Problem ? accent : Ink);

        var textLeft = inner.X + card.IconSide + card.IconGap;
        var textTop = inner.Y + (rowHeight - card.Title.Height - (card.Detail?.Height ?? 0)) / 2;
        context.DrawText(card.Title, new Point(textLeft, textTop));
        if (card.Detail is not null)
            context.DrawText(card.Detail, new Point(textLeft, textTop + card.Title.Height));
        if (card.Value is not null)
            context.DrawText(card.Value, new Point(inner.Right - card.Value.Width, textTop + (card.Title.Height - card.Value.Height) / 2));

        var y = inner.Y + rowHeight;
        if (card.Elapsed is not null)
        {
            y += 4;
            context.DrawText(card.Elapsed, new Point(inner.X, y));
            if (card.Remaining is not null)
                context.DrawText(card.Remaining, new Point(inner.Right - card.Remaining.Width, y));
            y += card.Elapsed.Height;
        }

        if (!card.HasBar)
            return;
        var bar = new Rect(inner.X, y + card.BarGap, inner.Width, card.BarThickness);
        context.DrawRectangle(Track, null, bar, 2, 2);
        if (notice.Level is { } level)
        {
            level = Math.Clamp(level, 0, 1);
            var from = notice.Centered ? Math.Min(0.5, level) : 0;
            var to = notice.Centered ? Math.Max(0.5, level) : level;
            if (to > from)
                context.DrawRectangle(accent, null, new Rect(bar.X + bar.Width * from, bar.Y, bar.Width * (to - from), bar.Height), 2, 2);
            if (notice.Centered)
                context.FillRectangle(Ink, new Rect(bar.Center.X - 1, bar.Y - 2, 2, bar.Height + 4));
        }
        else if (notice.Timeline is { Duration: > 0 } timeline)
        {
            context.DrawRectangle(accent, null, new Rect(bar.X, bar.Y, bar.Width * timeline.Fraction, bar.Height), 2, 2);
            // The A-B section: marks where it starts and ends.
            foreach (var point in new[] { timeline.LoopStart, timeline.LoopEnd })
            {
                if (point is { } seconds)
                    context.FillRectangle(Ink, new Rect(bar.X + bar.Width * Math.Clamp(seconds / timeline.Duration, 0, 1) - 1, bar.Y - 3, 2, bar.Height + 6));
            }

            context.DrawEllipse(Ink, null, new Point(bar.X + bar.Width * timeline.Fraction, bar.Center.Y), bar.Height, bar.Height);
        }
    }

    /// <summary>The texts of one card and the room they take. On a small skin it becomes one tight line (and its bar).</summary>
    private readonly struct Card
    {
        public readonly FormattedText Title;
        public readonly FormattedText? Detail, Value, Elapsed, Remaining;
        public readonly bool HasBar;
        public readonly double Padding, IconSide, IconGap, BarGap, BarThickness;
        public readonly Rect Bounds;

        public Card(Notice notice, NoticeFrame frame)
        {
            var scale = Math.Max(0.1, frame.Scale);
            var area = new Rect(frame.Area.X / scale, frame.Area.Y / scale, frame.Area.Width / scale, frame.Area.Height / scale);
            var topInset = frame.TopInset / scale;
            var bottomInset = frame.BottomInset / scale;
            var compact = area.Width < 270 || area.Height - topInset - bottomInset < 110;
            Padding = compact ? 6 : Pad;
            IconSide = compact ? 14 : IconSize;
            IconGap = compact ? 6 : Gap;
            BarGap = compact ? 3 : 8;
            BarThickness = compact ? 3 : BarHeight;

            var margin = compact ? 3.0 : Math.Clamp(area.Height * 0.04, 8, 24);
            var widest = Math.Max(60, Math.Min(440, area.Width - margin * 2));
            var textRoom = Math.Max(20, widest - Padding * 2 - IconSide - IconGap);

            // A track's position goes on the one line a small skin has room for.
            var value = notice.Value;
            if (compact && notice.Timeline is { } line)
                value = string.Join("  ", new[] { value, TimeLine(line) }.Where(s => !string.IsNullOrEmpty(s)));
            Value = string.IsNullOrEmpty(value) ? null : Text(value, compact ? 11 : TitleSize, Ink, textRoom * 0.65, bold: true);
            var valueRoom = Value is null ? 0 : Value.Width + IconGap;
            Title = Text(notice.Title, compact ? 11 : TitleSize, Ink, Math.Max(20, textRoom - valueRoom), bold: true);
            var detail = notice.Detail;
            if (compact && detail is not null && notice.Value is null && notice.Timeline is null)
                (Title, detail) = (Text($"{notice.Title}: {detail}", 11, Ink, textRoom, bold: true), null);
            Detail = compact || string.IsNullOrEmpty(detail) ? null : Text(detail, DetailSize, SubInk, textRoom);

            Elapsed = Remaining = null;
            if (!compact && notice.Timeline is { } timeline)
            {
                Elapsed = Text(TimeLine(timeline), TimeSize, Ink, widest);
                if (timeline.Duration > 0)
                    Remaining = Text(MediaPlayer.FormatTime(timeline.Remaining) + " left", TimeSize, SubInk, widest);
            }

            HasBar = notice.Level is not null || notice.Timeline is { Duration: > 0 };

            var row = IconSide + IconGap + Math.Max(Title.Width + valueRoom, Detail?.Width ?? 0);
            // Bars keep one width, so the card does not change size while a value or the time runs.
            var least = notice.Timeline is not null && !compact ? 300 : notice.Level is not null ? (compact ? 150 : 250) : 0;
            var width = Math.Min(widest, Math.Ceiling(Math.Max(row + Padding * 2, least)));
            var height = Padding * 2 + Math.Max(IconSide, Title.Height + (Detail?.Height ?? 0))
                + (Elapsed is null ? 0 : 4 + Elapsed.Height) + (HasBar ? BarGap + BarThickness : 0);
            height = Math.Ceiling(height);

            var x = frame.Place switch
            {
                NoticePlace.TopLeft => area.X + margin,
                NoticePlace.TopRight => area.Right - margin - width,
                _ => area.X + (area.Width - width) / 2,
            };
            var y = frame.Place switch
            {
                NoticePlace.Centre => area.Y + (area.Height - height) / 2,
                NoticePlace.Bottom => area.Bottom - bottomInset - margin - height,
                _ => area.Y + topInset + margin,
            };
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
            Bounds = new Rect(Math.Round(x), Math.Round(y), width, height);
        }

        private static string TimeLine(NoticeTimeline timeline) => timeline.Duration > 0
            ? $"{MediaPlayer.FormatTime(timeline.Position)} / {MediaPlayer.FormatTime(timeline.Duration)}"
            : $"{MediaPlayer.FormatTime(timeline.Position)} · Live";

        private static FormattedText Text(string text, double size, IBrush brush, double maxWidth, bool bold = false) =>
            new(text.ReplaceLineEndings(" "), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(bold), size, brush)
            {
                MaxTextWidth = Math.Max(1, maxWidth),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
    }

    // ---- Icons --------------------------------------------------------------------------------------------

    /// <summary>Icons the Fluent set here does not have, drawn on the same 24 px grid.</summary>
    private static readonly Dictionary<NoticeIcon, string> OwnIcons = new()
    {
        [NoticeIcon.Stop] = "M 6,6 H 18 V 18 H 6 Z",
        [NoticeIcon.FastForward] = "M 3,5.5 L 12,12 L 3,18.5 Z M 12.5,5.5 L 21.5,12 L 12.5,18.5 Z",
        [NoticeIcon.Rewind] = "M 21,5.5 L 12,12 L 21,18.5 Z M 11.5,5.5 L 2.5,12 L 11.5,18.5 Z",
        [NoticeIcon.Speed] = "M 12,4 A 10,10 0 0 0 2,14 A 10,10 0 0 0 3.6,19.4 L 5.4,18.2 A 8,8 0 0 1 4,14 A 8,8 0 0 1 20,14 A 8,8 0 0 1 18.6,18.2 L 20.4,19.4 A 10,10 0 0 0 22,14 A 10,10 0 0 0 12,4 Z M 10.6,13 L 17,8.4 L 13.4,15.4 A 1.7,1.7 0 1 1 10.6,13 Z",
        [NoticeIcon.Balance] = "M 2,9 H 6 L 10,5 V 19 L 6,15 H 2 Z M 22,9 H 18 L 14,5 V 19 L 18,15 H 22 Z",
        [NoticeIcon.Warning] = "F0 M 12,2.5 L 23,21.5 H 1 Z M 10.9,9 V 15 H 13.1 V 9 Z M 10.9,16.8 V 19 H 13.1 V 16.8 Z",
        [NoticeIcon.FullScreen] = "M 3,3 H 10 V 5 H 5 V 10 H 3 Z M 14,3 H 21 V 10 H 19 V 5 H 14 Z M 3,14 H 5 V 19 H 10 V 21 H 3 Z M 19,14 H 21 V 21 H 14 V 19 H 19 Z",
        [NoticeIcon.Window] = "F0 M 3,4 H 21 V 20 H 3 Z M 5,8.5 V 18 H 19 V 8.5 Z",
        [NoticeIcon.Marker] = "M 6,3 H 18 V 21 L 12,16.5 L 6,21 Z",
    };

    private static readonly Dictionary<NoticeIcon, string> FluentNames = new()
    {
        [NoticeIcon.Info] = "info",
        [NoticeIcon.Done] = "checkmark",
        [NoticeIcon.Play] = "play",
        [NoticeIcon.Pause] = "pause",
        [NoticeIcon.Previous] = "previous",
        [NoticeIcon.Next] = "next",
        [NoticeIcon.Shuffle] = "arrow_shuffle",
        [NoticeIcon.Repeat] = "arrow_repeat_all",
        [NoticeIcon.Sleep] = "clock",
        [NoticeIcon.Volume] = "speaker_2",
        [NoticeIcon.Muted] = "speaker_mute",
        [NoticeIcon.Equalizer] = "device_eq",
        [NoticeIcon.AudioTrack] = "mic",
        [NoticeIcon.Subtitles] = "closed_caption",
        [NoticeIcon.Lyrics] = "text_quote",
        [NoticeIcon.Picture] = "image",
        [NoticeIcon.Queue] = "text_bullet_list_add",
        [NoticeIcon.Heart] = "heart",
        [NoticeIcon.Music] = "music_note_2",
        [NoticeIcon.Video] = "video",
    };

    private static readonly Dictionary<NoticeIcon, Geometry> Shapes = [];

    private static Geometry ShapeOf(NoticeIcon icon)
    {
        if (!Shapes.TryGetValue(icon, out var shape))
        {
            shape = FluentNames.TryGetValue(icon, out var name) ? FluentIcons.Geo(name, filled: true)
                : Geometry.Parse(OwnIcons[icon] is var path && path.StartsWith("F0", StringComparison.Ordinal) ? path : "F1 " + OwnIcons[icon]);
            Shapes[icon] = shape;
        }

        return shape;
    }

    private static void DrawIcon(DrawingContext context, NoticeIcon icon, Rect at, IBrush brush)
    {
        var s = at.Width / 24;
        using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(at.X, at.Y)))
            context.DrawGeometry(brush, null, ShapeOf(icon));
    }
}
