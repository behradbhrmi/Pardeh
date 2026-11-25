using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The queue rows of the themed phone looks (PLAYLIST touch="true" rowStyle="..."): "amber" (Winamp Phone: one numbered line, the
/// playing song on a glossy amber bar with a play triangle) and "tiles" (Groove Phone: a picture tile, title over artist, the playing
/// song on an acrylic card with live bars).
/// </summary>
internal sealed partial class SkinRenderer
{
    private static LinearGradientBrush Down(params (string Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), at));
        return brush;
    }

    private static void DrawStyledRow(DrawingContext context, PlaylistElement list, MediaPlayer player, bool amber, int index, double y, double rowH)
    {
        var playlist = player.CurrentPlaylist;
        var item = playlist.Items[index];
        var playing = index == playlist.CurrentIndex && player.CurrentMedia is not null;
        var selected = index == list.SelectedIndex;
        var rightPad = 14.0 + (list.JumpVisible ? PlaylistElement.JumpGutter - 6 : 0);

        FormattedText Text(string text, Color color, double maxWidth, double size, bool bold = false) => new(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(bold), size, new SolidColorBrush(color))
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var card = new Rect(6, y + 2, list.Width - 12 - (list.JumpVisible ? PlaylistElement.JumpGutter - 6 : 0), rowH - 4);
        if (amber)
        {
            if (playing)
            {
                context.DrawRectangle(Down(("#FFD36A", 0), ("#FFA414", 0.45), ("#F08C00", 1)), new Pen(new SolidColorBrush(Color.Parse("#FFE4A0")), 1), new RoundedRect(card, 5));
            }
            else
            {
                if (selected)
                    context.FillRectangle(new SolidColorBrush(Color.Parse("#222C40")), card);
                // An engraved separator: a dark line with a light one under it.
                context.FillRectangle(new SolidColorBrush(Color.Parse("#03050A")), new Rect(10, y + rowH - 2, list.Width - 20, 1));
                context.FillRectangle(new SolidColorBrush(Color.Parse("#1C2536")), new Rect(10, y + rowH - 1, list.Width - 20, 1));
            }
        }
        else if (playing || selected)
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb((byte)(playing ? 66 : 34), 150, 200, 255)),
                new Pen(new SolidColorBrush(Color.FromArgb((byte)(playing ? 120 : 50), 170, 215, 255)), 1), new RoundedRect(card, 10));
        }

        var ink = amber ? (playing ? Color.Parse("#16100A") : Color.Parse("#E7EDF8")) : Colors.White;
        var dim = amber ? (playing ? Color.Parse("#3A2600") : Color.Parse("#9AA8BF")) : Color.Parse("#B7CCF2");
        var duration = item.Duration > TimeSpan.Zero ? Text(MediaPlayer.FormatTime(item.Duration.TotalSeconds), amber && playing ? ink : dim, 64, amber ? 13 : 12, amber && playing) : null;
        var favorite = player.IsFavorite(item);
        var right = list.Width - rightPad - (duration?.Width ?? 0);
        if (favorite)
        {
            right -= 22;
            DrawFavoriteMark(context, list, right + 2, y + (rowH - 16) / 2, 16);
        }

        if (playing && !amber)
        {
            right -= 24;
            Span<float> bars = stackalloc float[3];
            var feed = SpectrumFeed.For(player);
            feed.Update(SpectrumFeed.Now);
            feed.Group(bars);
            for (var bar = 0; bar < 3; bar++)
            {
                var height = 4 + bars[bar] * 12;
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#7FD4FF")), null, new RoundedRect(new Rect(right + 3 + bar * 6, y + rowH / 2 + 8 - height, 3.5, height), 1.5));
            }
        }

        if (amber)
        {
            if (playing)
            {
                var triangle = new StreamGeometry();
                using (var g = triangle.Open())
                {
                    g.BeginFigure(new Point(17, y + rowH / 2 - 6), true);
                    g.LineTo(new Point(28, y + rowH / 2));
                    g.LineTo(new Point(17, y + rowH / 2 + 6));
                    g.EndFigure(true);
                }

                context.DrawGeometry(new SolidColorBrush(ink), null, triangle);
            }

            var number = Text($"{index + 1}.", ink, 40, 14, playing);
            context.DrawText(number, new Point(Math.Max(34, 56 - number.Width), y + (rowH - number.Height) / 2));
            var title = Text(item.Name, ink, right - 66 - 8, 14.5, playing);
            context.DrawText(title, new Point(64, y + (rowH - title.Height) / 2));
        }
        else
        {
            var artist = item.GetAttribute("Author") ?? string.Empty;
            var tile = new Rect(14, y + (rowH - 42) / 2, 42, 42);
            CoverThumbs.Draw(context, tile, 7, CoverThumbs.Get(player, item), item.GetAttribute("Album") is { Length: > 0 } album ? album : item.Name);
            var width = right - 68 - 8;
            var title = Text(item.Name, Colors.White, width, 14.5);
            var under = Text(artist, dim, width, 12);
            var top = y + (rowH - title.Height - (artist.Length > 0 ? under.Height + 1 : 0)) / 2;
            context.DrawText(title, new Point(68, top));
            if (artist.Length > 0)
                context.DrawText(under, new Point(68, top + title.Height + 1));
        }

        if (duration is not null)
            context.DrawText(duration, new Point(list.Width - rightPad - duration.Width, y + (rowH - duration.Height) / 2));
    }
}
