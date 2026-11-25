using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>Draws a skin view pixel-for-pixel: bitmaps at their exact positions, no smoothing.</summary>
internal sealed partial class SkinRenderer
{

    /// <summary>Skin bitmaps are drawn pixel for pixel (no smoothing).</summary>
    /// <remarks>
    /// Set on each skin-bitmap draw, not around the whole view: with nested render options the outer one wins, so a
    /// view-wide "no smoothing" made video and visualizations scale with nearest-neighbour (jagged edges).
    /// </remarks>
    private static readonly RenderOptions ExactOptions = new() { BitmapInterpolationMode = BitmapInterpolationMode.None };
    private static readonly RenderOptions SmoothOptions = new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality };

    /// <summary>Android fit-to-screen scales skins by fractions: smooth their bitmaps instead of pixel-exact.</summary>
    public static bool SmoothScaling { get; set; }

    private static RenderOptions PixelExact => SmoothScaling ? SmoothOptions : ExactOptions;

    /// <summary>Where the last frame drew a Settings page (skin pixels, absolute), or null when none was shown.</summary>
    public Rect? SettingsHost { get; private set; }

    /// <summary>Where the last frame drew a phone Library page (skin pixels, absolute), or null.</summary>
    public Rect? LibraryHost { get; private set; }

    public void Render(DrawingContext context, SkinView view)
    {
        SettingsHost = null;
        LibraryHost = null;
        // Classic skins are pixel art drawn without edge smoothing. A skin that draws its own shapes for a dense screen (the phone
        // looks) asks for smooth edges with antialiasEdges="true" on its VIEW; otherwise the stepped edges are scaled up with the view.
        var edges = view.Root.GetBool("antialiasEdges") ? EdgeMode.Antialias : EdgeMode.Aliased;
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = edges }))
        {
            DrawElement(context, view.Root, view);
        }
    }


    /// <summary>Full screen: only the video, or the visualization stretched to the screen, on black.</summary>
    public void RenderFullScreen(DrawingContext context, SkinView view, Size size)
    {
        var bounds = new Rect(size);
        context.FillRectangle(Brushes.Black, bounds);
        var elements = AllElements(view).ToList();
        var player = view.Session.Player;
        if (player.HasVideo)
        {
            DrawVideo(context, elements.FirstOrDefault(e => e is VideoElement or DisplayElement) ?? view.Root, player, bounds, fullScreen: true);
            return;
        }

        if (elements.OfType<EffectsElement>().FirstOrDefault(e => e.Width > 0 && e.Height > 0) is { } effects)
        {
            DrawEffects(context, effects, player, bounds);
        }
    }

    /// <summary>Every element of a view, including the panes embedded in it.</summary>
    private static IEnumerable<SkinElement> AllElements(SkinView view)
    {
        foreach (var element in view.Elements)
        {
            yield return element;
            if (element is HostAreaElement { Embedded: { } pane })
            {
                foreach (var inner in AllElements(pane))
                    yield return inner;
            }
        }
    }
    private void DrawElement(DrawingContext context, SkinElement element, SkinView view)
    {
        if (!element.Visible || !element.IsVisual)
            return;

        var isRoot = element is ViewElement;
        // TEXT without a width sizes to its content, so it must not be clipped to zero.
        var width = element is TextElement && element.Width <= 0 ? 4096 : element.Width;
        // TEXT without an explicit height shows all its lines (captions like "Now / Playing").
        var height = element is TextElement && !element.ExplicitAttributes.Contains("height") ? 4096 : element.Height;
        var bounds = new Rect(0, 0, width, height);
        DrawingContext.PushedState? transform = isRoot ? null : context.PushTransform(Matrix.CreateTranslation(element.Left, element.Top));
        var clip = context.PushClip(bounds);
        DrawingContext.PushedState? opacity = element.Alpha < 255 ? context.PushOpacity(element.Alpha / 255.0) : null;
        try
        {
            DrawContent(context, element, view, bounds);
            foreach (var child in SkinView.PaintOrder(element))
                DrawElement(context, child, view);
        }
        finally
        {
            opacity?.Dispose();
            clip.Dispose();
            transform?.Dispose();
        }
    }

    /// <summary>Segoe UI (modern font) needs antialiased text; Tahoma stays aliased like the rest of the skin.</summary>
    private static DrawingContext.PushedState? UiText(DrawingContext context) =>
        UiTheme.ModernFont ? context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }) : null;

    private void DrawContent(DrawingContext context, SkinElement element, SkinView view, Rect bounds)
    {
        switch (element)
        {
            case ViewElement root:
                Fill(context, root.BackgroundColor, bounds);
                DrawBackground(context, root.BackgroundImage, root.BackgroundTiled, bounds);
                break;
            case SubviewElement subview:
                Fill(context, subview.BackgroundColor, bounds);
                if (subview.SliceBorder is { } slice && subview.BackgroundImage is { } patch)
                    DrawNinePatch(context, patch, slice, bounds);
                else
                    DrawBackground(context, subview.BackgroundImage, subview.BackgroundTiled, bounds);
                break;
            case ButtonElement button when button.GetBool("tiled") && button.CurrentImage is { } tile:
                Tile(context, tile, bounds);
                break;
            case ButtonElement button:
                Draw(context, button.CurrentImage, 0, 0);
                break;
            case ButtonGroupElement group:
                if (group.ShowBackground)
                    Draw(context, group.BaseImage, 0, 0);
                foreach (var item in group.Items)
                    Draw(context, group.GetOverlay(item), 0, 0);
                break;
            case SliderElement slider:
                DrawSlider(context, slider, slider.Kind == SliderKind.Seek ? view.Session.Player : null);
                break;
            case TextElement text:
                Fill(context, text.BackgroundColor, bounds);
                // Segoe UI (Pardeh's Fluent full mode) is made for antialiased text; XP-era fonts stay aliased.
                using (text.FontFamily?.Contains("Segoe", StringComparison.OrdinalIgnoreCase) == true
                    ? context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias })
                    : default(DrawingContext.PushedState?))
                    DrawText(context, text);
                break;
            case GlyphTextElement glyphs:
                DrawGlyphs(context, glyphs);
                break;
            case AnalyzerElement analyzer:
                DrawAnalyzer(context, analyzer);
                break;
            case EqBarsElement eqBars:
                DrawEqBars(context, eqBars, view.Session.Player, bounds);
                break;
            case VideoElement video:
                Fill(context, video.BackgroundColor, bounds);
                DrawVideo(context, video, view.Session.Player, bounds);
                break;
            case DisplayElement display when view.Session.Player.HasVideo:
                Fill(context, 0x000000, bounds);
                DrawVideo(context, display, view.Session.Player, bounds);
                break;
            case HostAreaElement host:
                Fill(context, host.BackgroundColor, bounds);
                if (host.Embedded is { } pane)
                    DrawElement(context, pane.Root, pane);
                else if (host.PluginKind.Contains("metadata") || host.PluginKind.Contains("mediainfo") || host.PluginKind.Contains("albumart"))
                {
                    using (UiText(context))
                        DrawMetadata(context, view.Session.Player, bounds);
                }

                break;
            case EffectsElement effects:
                DrawEffects(context, effects, view.Session.Player);
                break;
            case PlaylistElement playlist:
                using (UiText(context))
                    DrawPlaylist(context, playlist, view.Session.Player);
                break;
            case Pardeh.Skinning.Runtime.Panes.ListPaneElement { Model: Pardeh.Skinning.Runtime.Panes.SettingsModel } settingsPane:
                // The Settings page: the window lays its real controls over this rectangle (see SettingsHost).
                var (sx, sy) = settingsPane.AbsolutePosition;
                SettingsHost = new Rect(sx, sy, settingsPane.Width, settingsPane.Height);
                context.FillRectangle(Brushes.White, bounds);
                break;
            case Pardeh.Skinning.Runtime.Panes.ListPaneElement listPane:
                using (UiText(context))
                    DrawListPane(context, listPane);
                break;
            case LibraryElement library:
                using (UiText(context))
                    DrawLibrary(context, library, view.Session.Player);
                if (library.Phone)
                {
                    var (lx, ly) = library.AbsolutePosition;
                    LibraryHost = new Rect(lx, ly, library.Width, library.Height);
                }

                break;
            case DropDownPlaylistElement dropDown:
                using (UiText(context))
                    DrawDropDown(context, dropDown, view.Session.Player);
                break;
            case LyricsElement lyricsView:
                using (context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
                    DrawLyrics(context, lyricsView, view);
                break;
            case GenericElement generic:
                Fill(context, generic.BackgroundColor, bounds);
                break;
        }
    }

    private static void Fill(DrawingContext context, int? rgb, Rect rect)
    {
        if (rgb is { } color)
            context.FillRectangle(new SolidColorBrush(ToColor(color)), rect);
    }

    private static void Draw(DrawingContext context, SkinBitmap? bitmap, double x, double y)
    {
        if (bitmap is null)
            return;
        using (context.PushRenderOptions(OptionsFor(bitmap)))
            context.DrawImage(BitmapBridge.ToAvalonia(bitmap), new Rect(x, y, bitmap.LogicalWidth, bitmap.LogicalHeight));
    }

    /// <summary>High-resolution art (Scale > 1) is always drawn smoothed (it is shrunk to skin units); ordinary art follows <see cref="PixelExact"/>.</summary>
    private static RenderOptions OptionsFor(SkinBitmap bitmap) => bitmap.Scale > 1 ? SmoothOptions : PixelExact;

    private static void DrawBackground(DrawingContext context, SkinBitmap? bitmap, bool tiled, Rect bounds)
    {
        if (bitmap is null)
            return;
        if (!tiled)
        {
            Draw(context, bitmap, 0, 0);
            return;
        }

        Tile(context, bitmap, bounds);
    }

    /// <summary>SUBVIEW sliceBorder: corners drawn as they are, edges and middle stretched over <paramref name="area"/> (0: all stretched).</summary>
    private static void DrawNinePatch(DrawingContext context, SkinBitmap bitmap, double border, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return;
        var image = BitmapBridge.ToAvalonia(bitmap);
        using var smooth = context.PushRenderOptions(SmoothOptions);
        double s = bitmap.Scale, w = bitmap.LogicalWidth, h = bitmap.LogicalHeight;
        var b = Math.Min(border, Math.Min(Math.Min(w, h), Math.Min(area.Width, area.Height)) / 2);
        if (b <= 0)
        {
            context.DrawImage(image, new Rect(0, 0, w * s, h * s), area);
            return;
        }

        // Source columns / rows (image pixels) and the matching target ones (skin units).
        double[] sx = [0, b, w - b, w], sy = [0, b, h - b, h];
        double[] tx = [area.X, area.X + b, area.Right - b, area.Right], ty = [area.Y, area.Y + b, area.Bottom - b, area.Bottom];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var target = new Rect(tx[column], ty[row], Math.Max(0, tx[column + 1] - tx[column]), Math.Max(0, ty[row + 1] - ty[row]));
                if (target.Width > 0 && target.Height > 0)
                    context.DrawImage(image, new Rect(sx[column] * s, sy[row] * s, (sx[column + 1] - sx[column]) * s, (sy[row + 1] - sy[row]) * s), target);
            }
        }
    }

    private static void Tile(DrawingContext context, SkinBitmap bitmap, Rect area)
    {
        var image = BitmapBridge.ToAvalonia(bitmap);
        using var pixelExact = context.PushRenderOptions(OptionsFor(bitmap));
        double tileW = bitmap.LogicalWidth, tileH = bitmap.LogicalHeight;
        for (var y = area.Y; y < area.Bottom; y += tileH)
        {
            for (var x = area.X; x < area.Right; x += tileW)
                context.DrawImage(image, new Rect(x, y, tileW, tileH));
        }
    }

    private WriteableBitmap? _videoBitmap;
    private long _videoSequence = -1;

    private void DrawVideo(DrawingContext context, SkinElement video, MediaPlayer player, Rect bounds, bool fullScreen = false)
    {
        var frame = player.CurrentVideoFrame;
        if (frame is null || !player.HasVideo || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (_videoBitmap is null || _videoBitmap.PixelSize.Width != frame.Width || _videoBitmap.PixelSize.Height != frame.Height)
        {
            _videoBitmap?.Dispose();
            _videoBitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
            _videoSequence = -1;
        }

        // New frame, or the Video Settings changed while paused.
        var adjustments = player.Video;
        var adjustKey = (adjustments.Brightness, adjustments.Contrast, adjustments.Hue, adjustments.Saturation);
        if (frame.Sequence != _videoSequence || adjustKey != _videoAdjustKey)
        {
            using var buffer = _videoBitmap.Lock();
            var rowBytes = frame.Width * 4;
            if (adjustments.IsDefault)
            {
                for (var y = 0; y < frame.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
            }
            else
            {
                VideoAdjust.Apply(frame.Pixels, frame.Width, frame.Height, buffer.Address, buffer.RowBytes, adjustments);
            }

            _videoSequence = frame.Sequence;
            _videoAdjustKey = adjustKey;
        }

        var target = VideoTarget(video, frame, bounds, fullScreen);
        using (context.PushClip(bounds))
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                context.DrawImage(_videoBitmap, new Rect(0, 0, frame.Width, frame.Height), target);
            if (player.CurrentSubtitle is { } subtitle)
                DrawSubtitle(context, subtitle, target.Intersect(bounds), fullScreen);
        }
    }

    /// <summary>Subtitles: white with a dark outline, centred near the bottom of the picture, sized to it.</summary>
    private static void DrawSubtitle(DrawingContext context, string text, Rect picture, bool fullScreen)
    {
        var size = Math.Clamp(picture.Height / 18, 11, 44);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold), size, Brushes.White)
        {
            MaxTextWidth = Math.Max(1, picture.Width * 0.9),
            TextAlignment = TextAlignment.Center,
        };
        var origin = new Point(picture.X + (picture.Width - formatted.MaxTextWidth) / 2,
            picture.Bottom - formatted.Height - picture.Height * (fullScreen ? 0.1 : 0.05));
        var outline = formatted.BuildGeometry(origin);
        if (outline is not null)
            context.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)), Math.Max(2, size / 8), lineJoin: PenLineJoin.Round), outline);
        context.DrawText(formatted, origin);
    }

    private (double, double, double, double) _videoAdjustKey;

    /// <summary>
    /// Where the picture goes: fitted keeping its shape (the default), stretched to fill (Video Settings › Stretch),
    /// or at 50 / 100 / 200 % (never larger than the pane). Full screen always fits.
    /// </summary>
    private static Rect VideoTarget(SkinElement video, VideoFrame frame, Rect bounds, bool fullScreen)
    {
        var center = video.View.Session.MediaCenter;

        // The aspect ratio chosen by the user (View > Aspect Ratio, key A) wins over the skin's own setting.
        var aspect = Array.Find(VideoSettings.Aspects, a => a.Key == video.View.Session.Player.Video.Aspect);
        if (aspect.Ratio != 0)
        {
            if (aspect.Ratio == -1)
                return bounds; // stretched to the pane
            if (aspect.Ratio == -2)
            {
                // Cropped: the picture covers the pane, its overflow is clipped by the caller.
                var cover = Math.Max(bounds.Width / frame.Width, bounds.Height / frame.Height);
                double cw = frame.Width * cover, ch = frame.Height * cover;
                return new Rect(bounds.X + (bounds.Width - cw) / 2, bounds.Y + (bounds.Height - ch) / 2, cw, ch);
            }

            // A fixed ratio: the largest picture of that shape that fits the pane, centred.
            var width = Math.Min(bounds.Width, bounds.Height * aspect.Ratio);
            var height = width / aspect.Ratio;
            return new Rect(bounds.X + Math.Round((bounds.Width - width) / 2), bounds.Y + Math.Round((bounds.Height - height) / 2), Math.Round(width), Math.Round(height));
        }

        if (!fullScreen && SkinValue.ToBool(center.GetMember("videoStretchToFit")))
            return bounds;
        if (!video.GetBool("maintainAspectRatio", true))
            return bounds;

        var fit = Math.Min(bounds.Width / frame.Width, bounds.Height / frame.Height);
        var zoom = fullScreen ? 0 : SkinValue.ToDouble(center.GetMember("videoZoom"));
        var scale = zoom > 0 ? Math.Min(fit, zoom / 100) : fit;
        var w = Math.Round(frame.Width * scale);
        var h = Math.Round(frame.Height * scale);
        return new Rect(bounds.X + Math.Round((bounds.Width - w) / 2), bounds.Y + Math.Round((bounds.Height - h) / 2), w, h);
    }

    /// <summary>Notches on a seek bar at the track's markers and the A and B of a repeated section (nothing when there are none).</summary>
    private static void DrawSeekMarks(DrawingContext context, SliderElement slider, MediaPlayer player)
    {
        if (slider.Vertical || !slider.Enabled || player.Duration <= 0)
            return;
        var markers = player.CurrentMarkers;
        if (markers.Count == 0 && player.LoopStart is null)
            return;
        var thumbWidth = slider.ThumbRect.W;
        var height = Math.Min(12, slider.Height);
        var top = (slider.Height - height) / 2;
        double X(double seconds) => Math.Round(slider.Fraction(seconds) * Math.Max(0, slider.Width - thumbWidth) + thumbWidth / 2);
        var shade = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        void Notch(double seconds, IBrush brush)
        {
            var x = X(seconds);
            context.FillRectangle(shade, new Rect(x - 2, top - 1, 4, height + 2));
            context.FillRectangle(brush, new Rect(x - 1, top, 2, height));
        }

        if (player.LoopStart is { } a)
        {
            var b = player.LoopEnd;
            if (b is { } end)
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(80, 80, 220, 255)), new Rect(X(a), top + 2, Math.Max(0, X(end) - X(a)), height - 4));
        }

        foreach (var marker in markers)
            Notch(marker.Seconds, Brushes.White);
        if (player.LoopStart is { } start)
            Notch(start, new SolidColorBrush(Color.FromRgb(80, 220, 255)));
        if (player.LoopEnd is { } stop)
            Notch(stop, new SolidColorBrush(Color.FromRgb(80, 220, 255)));
    }

    private static void DrawSlider(DrawingContext context, SliderElement slider, MediaPlayer? player = null)
    {
        var bounds = new Rect(0, 0, slider.Width, slider.Height);
        var track = slider.TrackImage;
        if (track is not null)
            DrawSliderImage(context, slider, track, bounds);

        var thumb = slider.ThumbRect;
        var foreground = slider.ForegroundImage;
        if (foreground is not null && slider.Enabled)
        {
            // The filled part runs from the start to the middle of the thumb.
            Rect filled = slider.Vertical
                ? new Rect(0, thumb.Y + thumb.H / 2, slider.Width, slider.Height - (thumb.Y + thumb.H / 2))
                : new Rect(0, 0, thumb.X + thumb.W / 2, slider.Height);
            using (context.PushClip(filled))
                DrawSliderImage(context, slider, foreground, bounds);
        }

        if (player is not null)
            DrawSeekMarks(context, slider, player);
        Draw(context, slider.ThumbImage, thumb.X, thumb.Y);
    }


    /// <summary>
    /// A slider's track or fill: tiled, drawn as is, or — when it is a different length from the slider and the
    /// skin gives a borderSize — stretched with borderSize-pixel end caps kept intact.
    /// </summary>
    private static void DrawSliderImage(DrawingContext context, SliderElement slider, SkinBitmap image, Rect bounds)
    {
        if (slider.Tiled)
        {
            Tile(context, image, bounds);
            return;
        }

        var border = slider.BorderSize;
        var length = slider.Vertical ? bounds.Height : bounds.Width;
        var imageLength = slider.Vertical ? image.LogicalHeight : image.LogicalWidth;
        if (border <= 0 || Math.Abs(length - imageLength) < 1 || imageLength <= 2 * border)
        {
            Draw(context, image, 0, 0);
            return;
        }

        var bitmap = BitmapBridge.ToAvalonia(image);
        using var pixelExact = context.PushRenderOptions(OptionsFor(image));
        // Sources are in image pixels (skin units x Scale), targets in skin units.
        double s = image.Scale, w = image.LogicalWidth, h = image.LogicalHeight;
        if (slider.Vertical)
        {
            context.DrawImage(bitmap, new Rect(0, 0, w * s, border * s), new Rect(0, 0, w, border));
            context.DrawImage(bitmap, new Rect(0, border * s, w * s, (h - 2 * border) * s), new Rect(0, border, w, length - 2 * border));
            context.DrawImage(bitmap, new Rect(0, (h - border) * s, w * s, border * s), new Rect(0, length - border, w, border));
        }
        else
        {
            context.DrawImage(bitmap, new Rect(0, 0, border * s, h * s), new Rect(0, 0, border, h));
            context.DrawImage(bitmap, new Rect(border * s, 0, (w - 2 * border) * s, h * s), new Rect(border, 0, length - 2 * border, h));
            context.DrawImage(bitmap, new Rect((w - border) * s, 0, border * s, h * s), new Rect(length - border, 0, border, h));
        }
    }
    /// <summary>Bitmap-font text (Winamp classic skins): each glyph is a cell cut out of the skin's font sheet.</summary>
    private static void DrawGlyphs(DrawingContext context, GlyphTextElement glyphs)
    {
        if (glyphs.Sheet is not { } sheet)
            return;
        var bitmap = BitmapBridge.ToAvalonia(sheet);
        double w = glyphs.CellWidth, h = glyphs.CellHeight, s = sheet.Scale;
        using var pixelExact = context.PushRenderOptions(OptionsFor(sheet));
        foreach (var (sourceX, sourceY, x) in glyphs.Layout())
            context.DrawImage(bitmap, new Rect(sourceX * s, sourceY * s, w * s, h * s), new Rect(x, 0, w, h));
    }

    /// <summary>The Winamp classic analyzer: 3 px bars, coloured row by row from the skin's viscolor.txt, with peak dots.</summary>
    private static void DrawAnalyzer(DrawingContext context, AnalyzerElement analyzer)
    {
        var colors = analyzer.Colors;
        IBrush Brush(int rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        var bar = Enumerable.Range(0, AnalyzerElement.Rows).Select(row => Brush(colors[2 + row])).ToArray();
        var peak = Brush(colors[23]);
        for (var i = 0; i < AnalyzerElement.Bars; i++)
        {
            var x = i * AnalyzerElement.Pitch;
            var height = Math.Clamp(analyzer.Heights[i], 0, AnalyzerElement.Rows);
            // Row 0 is the top of the area; the bar grows up from the bottom, so its pixel rows use the lower colours.
            for (var row = AnalyzerElement.Rows - height; row < AnalyzerElement.Rows; row++)
                context.FillRectangle(bar[row], new Rect(x, row, AnalyzerElement.BarWidth, 1));
            var peakHeight = Math.Clamp(analyzer.Peaks[i], 0, AnalyzerElement.Rows);
            if (peakHeight > height)
                context.FillRectangle(peak, new Rect(x, AnalyzerElement.Rows - peakHeight, AnalyzerElement.BarWidth, 1));
        }
    }

    /// <summary>EQBARS: a live equaliser glyph; the bars follow the spectrum while playing and rest low and flat otherwise.</summary>
    private static void DrawEqBars(DrawingContext context, EqBarsElement element, MediaPlayer player, Rect bounds)
    {
        Span<float> levels = stackalloc float[element.Bars];
        if (element.Active)
        {
            var feed = SpectrumFeed.For(player);
            feed.Update(SpectrumFeed.Now);
            feed.Group(levels);
        }

        LiveBars.Draw(context, bounds, levels, LiveBarsLook.Of(element));
    }

    private static void DrawText(DrawingContext context, TextElement text)
    {
        var value = text.Text;
        if (value.Length == 0)
            return;

        var style = text.FontStyle.ToLowerInvariant();
        var typeface = new Typeface(
            FontFamily.Parse(NormalizeFont(text.FontFamily) + ", Tahoma, Arial"),
            style.Contains("italic") ? FontStyle.Italic : FontStyle.Normal,
            style.Contains("bold") ? FontWeight.Bold : FontWeight.Normal);

        var formatted = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            text.FontSize * 96 / 72, new SolidColorBrush(ToColor(text.ForegroundColor)));
        if (style.Contains("underline"))
            formatted.SetTextDecorations(TextDecorations.Underline);

        var width = text.Width;
        var justification = text.Justification.ToLowerInvariant();

        // Multi-line or word-wrapped text: lay out inside the element's width, aligning each line.
        if (width > 0 && (value.Contains('\n') || text.GetBool("wordWrap")))
        {
            formatted.MaxTextWidth = width;
            formatted.TextAlignment = justification switch
            {
                "right" => TextAlignment.Right,
                "center" => TextAlignment.Center,
                _ => TextAlignment.Left,
            };
            context.DrawText(formatted, new Point(0, 0));
            return;
        }

        var x = justification switch
        {
            "right" when width > 0 => width - formatted.Width,
            "center" when width > 0 => (width - formatted.Width) / 2,
            _ => 0,
        };

        context.DrawText(formatted, new Point(Math.Round(x), 0));
    }

    private static string NormalizeFont(string family) =>
        family.Equals("Ariel", StringComparison.OrdinalIgnoreCase) ? "Arial" : family; // a typo common in classic skins

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<EffectsElement, VisualizationRenderer> _visualizations = new();
    private readonly System.Diagnostics.Stopwatch _vizClock = System.Diagnostics.Stopwatch.StartNew();
    private double _vizLast;

    private void DrawEffects(DrawingContext context, EffectsElement effects, MediaPlayer player) =>
        DrawEffects(context, effects, player, new Rect(0, 0, effects.Width, effects.Height), chrome: true);

    private void DrawEffects(DrawingContext context, EffectsElement effects, MediaPlayer player, Rect bounds, bool chrome = false)
    {
        var now = _vizClock.Elapsed.TotalSeconds;
        var elapsed = now - _vizLast;
        _vizLast = now;
        var renderer = _visualizations.GetValue(effects, _ => new VisualizationRenderer());
        var glass = string.Equals(effects.GetString("palette"), "glass", StringComparison.OrdinalIgnoreCase); // opt-in: the glass look's blue bars
        renderer.Glass = glass;
        var index = effects.EffectIndex;
        var picker = effects.HasPicker;
        if (picker)
        {
            // The phone looks' picker: the style comes from VizDirector (saved default, swipes, automatic modes), not from the skin's script.
            VizDirector.Attach(player);
            VizDirector.Tick(elapsed, player.PlayState == PlayState.Playing, glass);
            if (glass)
            {
                renderer.GlassStyle = VizStyles.GlassStyle(VizDirector.GlassCurrent);
                renderer.Cover = renderer.GlassStyle == GlassVizStyle.Circular ? CoverDisc(effects, bounds) : null;
            }
            else
            {
                index = VizDirector.ClassicCurrent;
                if (effects.EffectIndex != index)
                    effects.SelectEffect(index);
            }
        }

        renderer.Render(context, bounds, index, player, elapsed);
        if (picker && chrome)
        {
            VizChrome.DrawFlash(context, bounds, glass);
            VizChrome.DrawChip(context, bounds, glass);
        }
    }

    /// <summary>The cover as a disc for the circular visualization (null: no real cover, the logo is shown).</summary>
    private static Avalonia.Media.IImage? CoverDisc(EffectsElement effects, Rect bounds)
    {
        var size = Math.Round(Math.Min(bounds.Width, bounds.Height) * 0.62);
        return size < 16 || effects.View.Session.CoverDisc(size, Math.Clamp(Pardeh.UI.FullMode.PhoneSkin.ArtScale, 1, 4)) is not { } disc
            ? null
            : BitmapBridge.ToAvalonia(disc);
    }

    private static void DrawPlaylist(DrawingContext context, PlaylistElement list, MediaPlayer player)
    {
        if (!list.GetBool("transparentBackground"))
            context.FillRectangle(new SolidColorBrush(ToColor(list.BackgroundColor)), new Rect(0, 0, list.Width, list.Height));
        var typeface = UiTheme.Typeface();
        var items = player.CurrentPlaylist.Items;
        if (list.Touch)
        {
            DrawTouchPlaylist(context, list, player);
            return;
        }

        var shown = list.Shown;
        var headerH = list.HeaderHeight;
        if (headerH > 0)
            DrawQueueHeader(context, list, headerH);
        var rows = list.VisibleRows + 1;
        for (var row = 0; row < rows; row++)
        {
            var position = list.ScrollOffset + row;
            if (position >= shown.Count)
                break;
            var index = shown[position];

            var y = headerH + row * PlaylistElement.RowHeight;
            if (index == list.SelectedIndex)
                context.FillRectangle(new SolidColorBrush(ToColor(list.SelectionColor)), new Rect(0, y, list.Width, PlaylistElement.RowHeight));

            var playing = index == player.CurrentPlaylist.CurrentIndex && player.CurrentMedia is not null;
            var brush = new SolidColorBrush(ToColor(playing ? list.PlayingColor : list.ForegroundColor));
            var duration = items[index].Duration > TimeSpan.Zero ? MediaPlayer.FormatTime(items[index].Duration.TotalSeconds) : string.Empty;
            var time = new FormattedText(duration, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, brush);
            var nameWidth = Math.Max(0, list.Width - time.Width - 8);
            var name = new FormattedText(items[index].Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, brush)
            {
                MaxTextWidth = nameWidth,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            context.DrawText(name, new Point(3, y + 1 + (PlaylistElement.RowHeight - 15) / 2));
            context.DrawText(time, new Point(list.Width - time.Width - 3, y + 1 + (PlaylistElement.RowHeight - 15) / 2));
        }
    }

    /// <summary>PLAYLIST headerHeight="N": a band above the rows with the queue size, its sort and its filter.</summary>
    private static void DrawQueueHeader(DrawingContext context, PlaylistElement list, double height)
    {
        var ink = ToColor(list.ForegroundColor);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), new Rect(0, 0, list.Width, height));
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(60, ink.R, ink.G, ink.B)), new Rect(0, height - 1, list.Width, 1));
        var text = new FormattedText(list.HeaderText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(), 12,
            new SolidColorBrush(Color.FromArgb(210, ink.R, ink.G, ink.B)))
        {
            MaxTextWidth = Math.Max(1, list.Width - 130),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(text, new Point(14, (height - text.Height) / 2));
    }

    /// <summary>The small heart that marks a favourite row: a Fluent heart on the modern looks, a glossy pictorial one on the vintage looks.</summary>
    private static void DrawFavoriteMark(DrawingContext context, PlaylistElement list, double x, double y, double size)
    {
        if (list.FluentHearts)
        {
            using (context.PushTransform(Matrix.CreateScale(size / 24, size / 24) * Matrix.CreateTranslation(x, y)))
                context.DrawGeometry(new SolidColorBrush(Color.Parse("#FF6B8A")), null, FluentIcons.Geo("heart", filled: true));
            return;
        }

        PictorialIcons.Draw(context, "heart", new Rect(x, y, size, size), shadow: false);
    }

    /// <summary>PLAYLIST touch="true": two-line rows (title, artist), a band and accent bar on the playing row.</summary>
    private static void DrawTouchPlaylist(DrawingContext context, PlaylistElement list, MediaPlayer player)
    {
        var playlist = player.CurrentPlaylist;
        var items = playlist.Items;
        list.RevealPlayingWhenChanged();
        var shown = list.Shown;
        var headerH = list.HeaderHeight;
        var style = list.GetString("rowStyle"); // opt-in themed rows: "amber" (Winamp Phone), "tiles" (Groove Phone)
        var styled = style is "amber" or "tiles";
        if (styled)
            context.FillRectangle(style == "amber" ? Down(("#0D121C", 0), ("#070B12", 1)) : Down(("#12489F", 0), ("#0B2E78", 0.55), ("#08235F", 1)), new Rect(0, 0, list.Width, list.Height));
        if (headerH > 0)
            DrawQueueHeader(context, list, headerH);
        var rowH = PlaylistElement.RowHeight;
        var ink = ToColor(list.ForegroundColor);
        var playingInk = ToColor(list.PlayingColor);
        var dim = Color.FromArgb(160, ink.R, ink.G, ink.B);
        FormattedText Text(string text, Color color, double maxWidth, double size, bool bold = false) => new(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(bold), size, new SolidColorBrush(color))
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        if (items.Count == 0)
        {
            context.DrawText(new FormattedText("The playlist is empty.\nAdd music from the Library.", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                UiTheme.Typeface(), 13, new SolidColorBrush(dim)) { MaxTextWidth = Math.Max(1, list.Width - 28) }, new Point(14, 16));
            return;
        }

        if (shown.Count == 0)
        {
            context.DrawText(new FormattedText("No songs in the queue match the filter.", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                UiTheme.Typeface(), 13, new SolidColorBrush(dim)) { MaxTextWidth = Math.Max(1, list.Width - 28) }, new Point(14, headerH + 16));
            return;
        }

        Span<float> liveBars = stackalloc float[3]; // the playing row's levels (Glass)
        for (var row = 0; row < list.VisibleRows + 1; row++)
        {
            var position = list.ScrollOffset + row;
            if (position >= shown.Count)
                break;
            var index = shown[position];
            var y = headerH + row * rowH;
            if (styled)
            {
                DrawStyledRow(context, list, player, style == "amber", index, y, rowH);
                continue;
            }

            var playing = index == playlist.CurrentIndex && player.CurrentMedia is not null;
            // numbered="true": the glass look: one-line rows, "1." in front of each, a glossy bar and a level meter on the playing row.
            var numbered = list.GetBool("numbered");
            if (playing && numbered)
            {
                var bar = new Rect(3, y + 1, list.Width - 11, rowH - 2);
                context.DrawRectangle(
                    new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(GlassPalette.Parse("#7CC4FF"), 0), new GradientStop(GlassPalette.Parse("#3C93F6"), 0.5), new GradientStop(GlassPalette.Parse("#1D6BE6"), 0.52), new GradientStop(GlassPalette.Parse("#1A5FD8"), 1) },
                    },
                    new Pen(new SolidColorBrush(GlassPalette.Parse("#9ED2FF")), 1), new RoundedRect(bar, 3));
            }
            else if (playing)
            {
                context.FillRectangle(new SolidColorBrush(ToColor(list.PlayingBackground)), new Rect(0, y, list.Width, rowH));
                context.FillRectangle(new SolidColorBrush(playingInk), new Rect(0, y + 6, 3, rowH - 12));
            }
            else if (index == list.SelectedIndex)
            {
                context.FillRectangle(new SolidColorBrush(ToColor(list.SelectionColor)), new Rect(0, y, list.Width, rowH));
            }

            var item = items[index];
            var duration = item.Duration > TimeSpan.Zero ? Text(MediaPlayer.FormatTime(item.Duration.TotalSeconds), numbered ? (playing ? playingInk : GlassPalette.Parse("#C9DDFF")) : playing ? playingInk : dim, 64, numbered ? 13 : 12) : null;
            var left = numbered ? 40.0 : 12.0;
            var rightPad = (numbered ? 16.0 : 12.0) + (list.JumpVisible ? PlaylistElement.JumpGutter - 6 : 0); // a long list keeps its times clear of the jump capsule
            var favorite = player.IsFavorite(item);
            var textW = list.Width - left - rightPad - (duration?.Width ?? 0) - (numbered ? 44 : 8) - (favorite ? 24 : 0);
            var title = Text(item.Name, playing && !numbered ? playingInk : ink, textW, 14, playing && !numbered);
            var artist = numbered ? string.Empty : item.GetAttribute("Author") ?? string.Empty;
            if (numbered)
            {
                var number = Text($"{index + 1}.", playing ? playingInk : ink, 30, 14);
                context.DrawText(number, new Point(12, y + (rowH - number.Height) / 2));
                if (playing && duration is not null)
                {
                    var mx = list.Width - rightPad - duration.Width - 18 - 15;
                    var baseY = y + rowH / 2 + 7;
                    // Live: three bars that follow the real spectrum while the track plays and sink to a low flat row when it is paused / stopped.
                    var feed = SpectrumFeed.For(player);
                    feed.Update(SpectrumFeed.Now);
                    feed.Group(liveBars);
                    LiveBars.Draw(context, new Rect(mx, baseY - 14, 16.5, 14), liveBars, LiveBarsLook.PlayingRow);
                }
            }

            context.DrawText(title, new Point(left, y + (artist.Length == 0 ? (rowH - title.Height) / 2 : 5)));
            if (artist.Length > 0)
                context.DrawText(Text(artist, dim, textW, 11), new Point(left, y + 5 + title.Height));
            if (duration is not null)
                context.DrawText(duration, new Point(list.Width - rightPad - duration.Width, y + (rowH - duration.Height) / 2));
            if (favorite)
                DrawFavoriteMark(context, list, list.Width - rightPad - (duration?.Width ?? 0) - 22 - (numbered && playing ? 33 : 0), y + (rowH - 16) / 2, 16);
            if (!numbered)
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40, ink.R, ink.G, ink.B)), 1), new Point(12, y + rowH - 0.5), new Point(list.Width, y + rowH - 0.5));
        }

        // numbered: a thin scrollbar at the right edge when the list does not fit.
        if (list.GetBool("numbered") && shown.Count > list.VisibleRows)
        {
            var track = new Rect(list.Width - 7, 3, 4, list.Height - 6);
            context.DrawRectangle(new SolidColorBrush(GlassPalette.Map(Color.FromArgb(70, 5, 20, 80))), null, new RoundedRect(track, 2));
            var thumbH = Math.Max(16, track.Height * list.VisibleRows / shown.Count);
            var thumbY = track.Y + (track.Height - thumbH) * list.ScrollOffset / Math.Max(1, shown.Count - list.VisibleRows);
            context.DrawRectangle(new SolidColorBrush(GlassPalette.Parse("#7DB4FA")), null, new RoundedRect(new Rect(track.X, thumbY, track.Width, thumbH), 2));
        }

        if (list.JumpVisible)
            DrawJumpButtons(context, list);
    }

    /// <summary>The first / playing / last song buttons of a long touch list: one slim glass capsule in the skin colour with three segments, in a gutter the rows leave free.</summary>
    private static void DrawJumpButtons(DrawingContext context, PlaylistElement list)
    {
        var ink = ToColor(list.ForegroundColor);
        var glass = list.GetBool("numbered"); // the Glass look: its blue gloss, tinted by the skin colour
        string[] glyphs =
        [
            "M 6,14 L 12,8 L 18,14 M 7,5.5 L 17,5.5",   // first: chevron up under a bar
            "M 12,12 m -3.4,0 a 3.4,3.4 0 1,0 6.8,0 a 3.4,3.4 0 1,0 -6.8,0 M 12,12 m -1,0 a 1,1 0 1,0 2,0 a 1,1 0 1,0 -2,0", // now playing: a ring and a dot
            "M 6,10 L 12,16 L 18,10 M 7,18.5 L 17,18.5", // last: chevron down over a bar
        ];
        var (x, top, size) = list.JumpRect(0);
        var capsule = new Rect(x, top, size, size * 3);
        IBrush fill = glass
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(235, GlassPalette.Parse("#3C93F6").R, GlassPalette.Parse("#3C93F6").G, GlassPalette.Parse("#3C93F6").B), 0), new GradientStop(Color.FromArgb(235, GlassPalette.Parse("#1A5FD8").R, GlassPalette.Parse("#1A5FD8").G, GlassPalette.Parse("#1A5FD8").B), 1) },
            }
            : new SolidColorBrush(Color.FromArgb(215, 30, 34, 44));
        var rim = new Pen(new SolidColorBrush(glass ? GlassPalette.Parse("#BBDCFF") : Color.FromArgb(120, ink.R, ink.G, ink.B)), 1.2);
        context.DrawRectangle(fill, rim, new RoundedRect(capsule, size / 2));
        // A soft highlight along the top half, like the glossy pills.
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), null, new RoundedRect(new Rect(capsule.X + 2, capsule.Y + 2, capsule.Width - 4, capsule.Height * 0.4), size / 2 - 2));
        for (var button = 0; button < 3; button++)
        {
            var (bx, by, bs) = list.JumpRect(button);
            if (button > 0)
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1), new Point(bx + 7, by), new Point(bx + bs - 7, by));
            var stroke = new Pen(Brushes.White, 1.9, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(bs * 0.6 / 24, bs * 0.6 / 24) * Matrix.CreateTranslation(bx + bs * 0.2, by + bs * 0.2)))
                context.DrawGeometry(null, stroke, Geometry.Parse(glyphs[button]));
        }
    }

    /// <summary>The metadata plug-in: what's playing, in the classic white-on-blue style.</summary>
    private static void DrawMetadata(DrawingContext context, MediaPlayer player, Rect bounds)
    {
        if (player.CurrentMedia is not { } media)
            return;
        var lines = new[]
        {
            (media.Name, true),
            (media.GetAttribute("Author"), false),
            (media.GetAttribute("Album"), false),
        };
        var y = 8.0;
        foreach (var (text, bold) in lines)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                UiTheme.Typeface(bold), 11, Brushes.White)
            {
                MaxTextWidth = Math.Max(1, bounds.Width - 16),
                MaxLineCount = 2,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            context.DrawText(formatted, new Point(8, y));
            y += formatted.Height + 2;
        }
    }

    private static readonly IBrush LibraryBack = new SolidColorBrush(Color.Parse("#1F2A5C"));
    private static readonly IBrush LibraryHeader = new SolidColorBrush(Color.Parse("#ECE9D8"));
    private static readonly IBrush LibraryHeaderInk = new SolidColorBrush(Color.Parse("#000000"));
    private static readonly IBrush LibraryInk = Brushes.White;
    private static readonly IBrush LibraryPlaying = new SolidColorBrush(Color.Parse("#7FFF00"));
    private static readonly IBrush LibrarySelection = new SolidColorBrush(Color.Parse("#0A246A"));
    private static readonly IBrush LibraryTreeSelection = new SolidColorBrush(Color.Parse("#ECE9D8"));
    private static readonly IPen LibraryRule = new Pen(new SolidColorBrush(Color.Parse("#ACA899")), 1);

    // The classic library's colours (measured from a 2003 screenshot).
    private static readonly IBrush LibraryHeaderBand = new SolidColorBrush(Color.Parse("#E9E7CE"));
    private static readonly IBrush LibraryPlayingInk = new SolidColorBrush(Color.Parse("#9ACD32"));
    private static readonly IBrush LibraryDisabledInk = new SolidColorBrush(Color.Parse("#8E99D0"));
    private static readonly IBrush LibraryHintInk = new SolidColorBrush(Color.Parse("#C9D3F0"));
    private static readonly IBrush LibraryStarInk = new SolidColorBrush(Color.Parse("#F5C518"));
    private static readonly IBrush LibraryStarDim = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

    /// <summary>The phone layout of the library (LIBRARY phone="true"): one column, touch rows, a compact toolbar.</summary>
    private static void DrawPhoneLibrary(DrawingContext context, LibraryElement library, MediaPlayer player)
    {
        double w = library.Width, h = library.Height, barH = library.TopBarHeight, rowH = library.RowH, footH = library.BottomBarHeight;
        Color Hue(string name, string fallback) => Color.Parse(library.GetString(name) is { Length: > 0 } s ? s : fallback);
        var back = Hue("libBack", "#1C1C1C");
        var bar = Hue("libBar", "#202020");
        var barInkC = Hue("libBarInk", "#FFFFFF");
        var inkC = Hue("libInk", "#FFFFFF");
        var dimC = Hue("libDim", "#9D9D9D");
        var selC = Hue("libSel", "#3A3A3A");
        var accentC = Hue("libAccent", "#60CDFF");
        IBrush Fill(Color c) => new SolidColorBrush(c);
        var rule = new Pen(Fill(Color.FromArgb(70, dimC.R, dimC.G, dimC.B)), 1);
        FormattedText Text(string text, Color color, double maxWidth, double size, bool bold = false, bool wrap = false) => new(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(bold), size, Fill(color))
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = wrap ? 0 : 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var listTop = barH;
        var bodyBottom = h - footH;
        context.FillRectangle(Fill(back), new Rect(0, 0, w, h));

        var nodes = library.Nodes;
        var tracks = library.Tracks;
        var playing = player.CurrentMedia?.Source;
        using (context.PushClip(new Rect(0, listTop, w, Math.Max(0, bodyBottom - listTop))))
        {
            if (!library.ShowList)
            {
                for (var row = 0; row < library.VisibleRows + 1; row++)
                {
                    var index = library.TreeScroll + row;
                    if (index >= nodes.Count)
                        break;
                    var node = nodes[index];
                    var y = listTop + row * rowH;
                    var isSel = node.Key == library.SelectedNodeKey;
                    if (isSel)
                    {
                        context.FillRectangle(Fill(Color.FromArgb(90, selC.R, selC.G, selC.B)), new Rect(0, y, w, rowH));
                        context.FillRectangle(Fill(accentC), new Rect(0, y + 10, 3, rowH - 20));
                    }

                    var label = Text(node.Title, node.Depth == 0 ? inkC : Color.FromArgb(235, inkC.R, inkC.G, inkC.B), w - 70 - node.Depth * 18, 15, node.Depth == 0);
                    context.DrawText(label, new Point(16 + node.Depth * 18, y + (rowH - label.Height) / 2));
                    if (node.HasChildren)
                    {
                        var cx = w - 28;
                        var cy = y + rowH / 2;
                        var chevron = new Pen(Fill(dimC), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                        var open = library.IsExpanded(node.Key);
                        context.DrawLine(chevron, open ? new Point(cx - 6, cy - 3) : new Point(cx - 3, cy - 6), open ? new Point(cx, cy + 3) : new Point(cx + 3, cy));
                        context.DrawLine(chevron, open ? new Point(cx, cy + 3) : new Point(cx + 3, cy), open ? new Point(cx + 6, cy - 3) : new Point(cx - 3, cy + 6));
                    }

                    context.DrawLine(rule, new Point(16, y + rowH - 0.5), new Point(w, y + rowH - 0.5));
                }
            }
            else
            {
                for (var row = 0; row < library.VisibleRows + 1; row++)
                {
                    var index = library.TrackScroll + row;
                    if (index >= tracks.Count)
                        break;
                    var track = tracks[index];
                    var y = listTop + row * rowH;
                    var isPlaying = string.Equals(track.Path, playing, StringComparison.OrdinalIgnoreCase);
                    if (index == library.SelectedTrack)
                        context.FillRectangle(Fill(Color.FromArgb(120, selC.R, selC.G, selC.B)), new Rect(0, y, w, rowH));
                    if (isPlaying)
                        context.FillRectangle(Fill(accentC), new Rect(0, y + 8, 3, rowH - 16));

                    var time = track.DurationSeconds > 0 ? Text(MediaPlayer.FormatTime(track.DurationSeconds), dimC, 70, 12) : null;
                    var textW = w - 28 - (time?.Width ?? 0) - 8;
                    var title = Text(track.Title, isPlaying ? accentC : inkC, textW, 15, isPlaying);
                    var sub = string.Join(" - ", new[] { track.Artist, track.Album }.Where(s => s.Length > 0));
                    var subText = Text(sub.Length > 0 ? sub : (track.IsVideo ? "Video" : "Unknown artist"), dimC, textW, 11.5);
                    context.DrawText(title, new Point(16, y + 6));
                    context.DrawText(subText, new Point(16, y + 6 + title.Height + 1));
                    if (time is not null)
                        context.DrawText(time, new Point(w - 14 - time.Width, y + (rowH - time.Height) / 2));
                    context.DrawLine(rule, new Point(16, y + rowH - 0.5), new Point(w, y + rowH - 0.5));
                }

                if (tracks.Count == 0)
                    context.DrawText(Text(library.EmptyText, dimC, w - 40, 14, wrap: true), new Point(20, listTop + 24));
            }
        }

        // Toolbar: back arrow (list) or title, then queue / search / add as 48 px touch buttons on the right.
        context.FillRectangle(Fill(bar), new Rect(0, 0, w, barH));
        context.DrawLine(rule, new Point(0, barH - 0.5), new Point(w, barH - 0.5));
        var titleX = 16.0;
        if (library.ShowList)
        {
            var arrow = new Pen(Fill(barInkC), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawLine(arrow, new Point(27, barH / 2 - 9), new Point(18, barH / 2));
            context.DrawLine(arrow, new Point(18, barH / 2), new Point(27, barH / 2 + 9));
            titleX = 44;
        }

        var heading = library.ShowList ? nodes.FirstOrDefault(n => n.Key == library.SelectedNodeKey)?.Title ?? "Library" : "Library";
        var headingText = Text(heading, barInkC, Math.Max(40, w - titleX - (library.ShowList ? 150 : 100)), 17, bold: true);
        context.DrawText(headingText, new Point(titleX, (barH - headingText.Height) / 2));

        library.ButtonBounds.Clear();
        for (var i = 0; i < LibraryElement.Tools.Count; i++)
            library.ButtonBounds.Add((-1000, 0));
        void Button(string key, double x)
        {
            var i = LibraryElement.Tools.ToList().FindIndex(t => t.Key == key);
            var enabled = library.IsToolEnabled(key);
            library.ButtonBounds[i] = (x, 48);
            var c = new Point(x + 24, barH / 2);
            if (i == library.PressedButton)
                context.DrawEllipse(Fill(Color.FromArgb(60, barInkC.R, barInkC.G, barInkC.B)), null, c, 21, 21);
            var color = enabled ? barInkC : Color.FromArgb(110, barInkC.R, barInkC.G, barInkC.B);
            var pen = new Pen(Fill(color), 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            switch (key)
            {
                case "search":
                    context.DrawEllipse(null, pen, new Point(c.X - 2, c.Y - 2), 7, 7);
                    context.DrawLine(pen, new Point(c.X + 3, c.Y + 3), new Point(c.X + 9, c.Y + 9));
                    break;
                case "add":
                    context.DrawLine(pen, new Point(c.X - 8, c.Y), new Point(c.X + 8, c.Y));
                    context.DrawLine(pen, new Point(c.X, c.Y - 8), new Point(c.X, c.Y + 8));
                    break;
                case "queue":
                    context.DrawLine(pen, new Point(c.X - 9, c.Y - 6), new Point(c.X + 3, c.Y - 6));
                    context.DrawLine(pen, new Point(c.X - 9, c.Y), new Point(c.X + 3, c.Y));
                    context.DrawLine(pen, new Point(c.X - 9, c.Y + 6), new Point(c.X - 2, c.Y + 6));
                    context.DrawLine(pen, new Point(c.X + 7, c.Y + 1), new Point(c.X + 7, c.Y + 9));
                    context.DrawLine(pen, new Point(c.X + 3, c.Y + 5), new Point(c.X + 11, c.Y + 5));
                    break;
            }
        }

        Button("add", w - 48);
        Button("search", w - 96);
        if (library.ShowList)
            Button("queue", w - 144);

        // Footer: how many items the open list has.
        if (footH > 0)
        {
            context.FillRectangle(Fill(bar), new Rect(0, bodyBottom, w, footH));
            context.DrawLine(rule, new Point(0, bodyBottom + 0.5), new Point(w, bodyBottom + 0.5));
            var count = Text(tracks.Count == 1 ? "1 item" : $"{tracks.Count} items", dimC, w / 2, 11);
            context.DrawText(count, new Point(16, bodyBottom + (footH - count.Height) / 2));
            var total = Text(library.TotalTimeText, dimC, w / 2, 11);
            context.DrawText(total, new Point(w - 16 - total.Width, bodyBottom + (footH - total.Height) / 2));
        }
    }

    private static void DrawLibrary(DrawingContext context, LibraryElement library, MediaPlayer player)
    {
        if (library.Phone)
        {
            DrawPhoneLibrary(context, library, player); // the base layer; the window lays the touch library over it (LibraryHost)
            return;
        }

        double w = library.Width, h = library.Height;
        var toolbarH = library.TopBarHeight;
        const double headerH = LibraryElement.HeaderHeight;
        double rowH = LibraryElement.RowHeight;
        var footerH = library.BottomBarHeight;
        var light = library.Light;
        // Opt-in colours of a look's own (libBack and friends, the PC retro looks); null for every other look.
        var own = PaneColors.Of(library);
        // Opt-in light palette (LIBRARY light="true"): the WMP 10 look. Defaults keep the dark blue one.
        var headerBand = own?.Bar ?? (light ? Solid("#EFEFEF") : LibraryHeaderBand);
        var headerInk = own?.BarInk ?? Brushes.Black;
        var treeInk = own?.Ink ?? (light ? Solid("#1B2A55") : Brushes.White);
        var listInk = own?.Ink ?? (light ? Solid("#1B2A55") : Brushes.White);
        var hintInk = own?.Dim ?? (light ? Solid("#1B3A8C") : LibraryHintInk);
        var treeSelBack = own?.Selection ?? (light ? Solid("#CFDDF6") : LibraryHeaderBand);
        var treeSelInk = own?.Ink ?? (light ? Solid("#0E2A7A") : Brushes.Black);
        var playingInk = own?.Accent ?? (light ? Solid("#1B7A1B") : LibraryPlayingInk);
        var expandPen = new Pen(light ? Solid("#4A5A8A") : Brushes.Black, 1);
        var tree = library.TreeWidth;
        var listX = library.ListLeft;
        var listTop = toolbarH + headerH;
        var bodyBottom = h - footerH;
        FormattedText Text(string text, IBrush brush, double maxWidth, bool bold = false) => new(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            UiTheme.Typeface(bold), 11, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var session = library.View.Session;
        var pane = own?.Back ?? (light ? Brushes.White : Tinted(session, "#202C66"));
        context.FillRectangle(pane, new Rect(0, 0, w, h));
        if (light && own is null)
            context.FillRectangle(Solid("#EDF3FD"), new Rect(listX, 0, w - listX, h));

        // Toolbar: icon + label buttons, flat on the blue band; separators between groups.
        context.FillRectangle(own?.Bar ?? Tinted(session, "#4154B0"), new Rect(0, 0, w, toolbarH));
        library.ButtonBounds.Clear();
        var x = 6.0;
        for (var i = 0; i < (toolbarH > 0 ? LibraryElement.Tools.Count : 0); i++)
        {
            var tool = LibraryElement.Tools[i];
            if (tool.Key is "search" or "albuminfo")
            {
                context.DrawLine(new Pen(Solid("#2C3B8C"), 1), new Point(x + 0.5, 5), new Point(x + 0.5, toolbarH - 5));
                context.DrawLine(new Pen(Solid("#6D7FD0"), 1), new Point(x + 1.5, 5), new Point(x + 1.5, toolbarH - 5));
                x += 6;
            }

            var enabled = library.IsToolEnabled(tool.Key);
            var label = tool.ShowLabel ? Text(tool.Label, enabled ? Brushes.White : LibraryDisabledInk, 140) : null;
            var bw = 22 + (label is null ? 0 : label.Width + 4) + (tool.IsMenu ? 8 : 0);
            if (x + bw > w - 4)
                break;
            var rect = new Rect(x, 3, bw, toolbarH - 6);
            if (i == library.PressedButton)
                context.DrawRectangle(Solid("#2C3B8C"), new Pen(Solid("#8C9BE0"), 1), rect.Deflate(0.5), 2, 2);
            DrawLibraryToolIcon(context, tool.Key, new Rect(x + 3, (toolbarH - 16) / 2, 16, 16), enabled);
            if (label is not null)
                context.DrawText(label, new Point(x + 21, (toolbarH - label.Height) / 2));
            if (tool.IsMenu)
                DrawSmallArrow(context, new Point(rect.Right - 7, toolbarH / 2), Brushes.White);
            library.ButtonBounds.Add((x, bw));
            x += bw + 2;
        }

        // Header band: the selected node's name over the tree, column titles over the list.
        context.FillRectangle(headerBand, new Rect(0, toolbarH, w, headerH));
        context.DrawLine(LibraryRule, new Point(0, listTop - 0.5), new Point(w, listTop - 0.5));
        var nodes = library.Nodes;
        var selectedTitle = nodes.FirstOrDefault(n => n.Key == library.SelectedNodeKey)?.Title ?? "Library";
        context.DrawText(Text(selectedTitle, headerInk, tree - 10), new Point(5, toolbarH + 2));
        var columns = library.ColumnBounds;
        for (var c = 0; c < columns.Count; c++)
        {
            var (cx, cw) = columns[c];
            var arrow = c == library.SortColumn ? (library.SortDescending ? " ▼" : " ▲") : string.Empty;
            context.DrawText(Text(LibraryElement.Columns[c] + arrow, headerInk, cw - 8), new Point(cx + 5, toolbarH + 2));
            context.DrawLine(LibraryRule, new Point(cx - 0.5, toolbarH + 3), new Point(cx - 0.5, listTop - 3));
        }

        // Tree.
        using (context.PushClip(new Rect(0, listTop, tree, bodyBottom - listTop)))
        {
            for (var row = 0; row < library.VisibleRows + 1; row++)
            {
                var index = library.TreeScroll + row;
                if (index >= nodes.Count)
                    break;
                var node = nodes[index];
                var y = listTop + row * rowH + 1 + (rowH - 15) / 2;
                var nx = 4 + node.Depth * 14;
                if (node.HasChildren)
                {
                    var box = new Rect(nx + 0.5, y + 2.5, 8, 8);
                    context.DrawRectangle(Brushes.White, new Pen(Solid("#7F9DB9"), 1), box);
                    context.DrawLine(expandPen, new Point(nx + 2.5, y + 6.5), new Point(nx + 6.5, y + 6.5));
                    if (!library.IsExpanded(node.Key))
                        context.DrawLine(expandPen, new Point(nx + 4.5, y + 4.5), new Point(nx + 4.5, y + 8.5));
                }

                var iconX = nx + 13;
                if (node.Icon.Length > 0)
                    DrawLibraryNodeIcon(context, node.Icon, new Rect(iconX, y - 1, 16, 14));
                var labelX = iconX + (node.Icon.Length > 0 ? 19 : 0);
                var isSelected = node.Key == library.SelectedNodeKey;
                var label = Text(node.Title, isSelected ? treeSelInk : treeInk, tree - labelX - 4);
                if (isSelected)
                    context.FillRectangle(treeSelBack, new Rect(labelX - 2, y, label.Width + 4, rowH - 2));
                context.DrawText(label, new Point(labelX, y));
            }
        }

        // Splitter between tree and list.
        context.FillRectangle(own?.Bar ?? Solid(light ? "#5B86D8" : "#E5E7E4"), new Rect(tree, listTop, LibraryElement.SplitterWidth, h - listTop));
        context.DrawLine(new Pen(own?.Bar ?? Solid(light ? "#2F52B8" : "#B3BDBF"), 1),new Point(tree + LibraryElement.SplitterWidth - 0.5, listTop), new Point(tree + LibraryElement.SplitterWidth - 0.5, h));

        // Track list: the playing item on a black band in green, selection in blue.
        var tracks = library.Tracks;
        var playing = player.CurrentMedia?.Source;
        using (context.PushClip(new Rect(listX, listTop, w - listX, bodyBottom - listTop)))
        {
            for (var row = 0; row < library.VisibleRows + 1; row++)
            {
                var index = library.TrackScroll + row;
                if (index >= tracks.Count)
                    break;
                var track = tracks[index];
                var y = listTop + row * rowH;
                var isPlaying = string.Equals(track.Path, playing, StringComparison.OrdinalIgnoreCase);
                if (index == library.SelectedTrack)
                    context.FillRectangle(own?.Selection ?? Tinted(session, "#316AC5"), new Rect(listX, y, w - listX, rowH));
                else if (isPlaying)
                    context.FillRectangle(own?.Selection ?? (light ? Solid("#DCE6F8") : Brushes.Black), new Rect(listX, y, w - listX, rowH));
                var ink = isPlaying ? playingInk : light && own is null && index == library.SelectedTrack ? Brushes.White : listInk;
                string[] cells = [track.Title, track.Artist, track.Album, string.Empty, track.Genre, track.DurationSeconds > 0 ? MediaPlayer.FormatTime(track.DurationSeconds) : string.Empty];
                for (var c = 0; c < columns.Count; c++)
                {
                    if (c == LibraryElement.RatingColumn)
                    {
                        // Five stars: gold for the rating, dim for the rest (click to rate).
                        for (var s = 0; s < 5; s++)
                        {
                            var star = Text("★", s < track.Rating ? LibraryStarInk : own?.Faint ?? (light ? Solid("#C5CFE6") : LibraryStarDim), LibraryElement.StarWidth + 2);
                            context.DrawText(star, new Point(columns[c].X + 4 + s * LibraryElement.StarWidth, y + (rowH - 15) / 2));
                        }

                        continue;
                    }

                    var cell = Text(cells[c], ink, columns[c].Width - 8);
                    var cellX = c == columns.Count - 1 ? columns[c].X + columns[c].Width - cell.Width - 6 : columns[c].X + 4;
                    context.DrawText(cell, new Point(cellX, y + 1 + (rowH - 15) / 2));
                }
            }

            if (tracks.Count == 0)
                context.DrawText(Text(library.EmptyText, hintInk, w - listX - 20), new Point(listX + 8, listTop + (light ? 20 : 8)));
        }

        // Footer.
        if (footerH <= 0)
            return;
        context.FillRectangle(own?.Bar ?? Tinted(session, "#0F1338"), new Rect(listX, bodyBottom, w - listX, footerH));
        context.DrawText(Text(tracks.Count == 1 ? "1 Item" : $"{tracks.Count} Items", own?.BarInk ?? Brushes.White, 120), new Point(listX + 5, bodyBottom + 2));
        var totals = Text(library.FooterTotals, own?.BarInk ?? Brushes.White, 260);
        context.DrawText(totals, new Point(w - totals.Width - 6, bodyBottom + 2));
    }

    private static void DrawSmallArrow(DrawingContext context, Point center, IBrush brush) =>
        context.DrawGeometry(brush, null, Poly(new Point(center.X - 3, center.Y - 1.5), new Point(center.X + 3, center.Y - 1.5), new Point(center.X, center.Y + 1.5)));

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    /// <summary>A colour of the glass look through the skin colour setting (<see cref="GlassPalette"/>); other looks are returned as they are.</summary>
    private static Color Tinted(Color color, bool glass) => glass ? GlassPalette.Map(color) : color;

    /// <summary>A pane colour through the colour chooser (mediacenter.backHueShift / backSaturation), like the chrome.</summary>
    private static IBrush Tinted(SkinSession session, string color)
    {
        var hue = SkinValue.ToDouble(session.MediaCenter.GetMember("backHueShift"));
        var saturation = SkinValue.ToDouble(session.MediaCenter.GetMember("backSaturation"), 1);
        var rgb = Pardeh.Skinning.Imaging.SkinBitmap.ShiftColor(int.Parse(color.TrimStart('#'), System.Globalization.NumberStyles.HexNumber), hue, saturation);
        return new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    private static Geometry Poly(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true);
            foreach (var p in points.Skip(1))
                g.LineTo(p);
            g.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>The library toolbar's small icons (Pardeh's own drawings in the classic style).</summary>
    private static void DrawLibraryToolIcon(DrawingContext context, string key, Rect r, bool enabled)
    {
        double x = r.X, y = r.Y;
        var grey = Solid("#8E99D0");
        switch (key)
        {
            case "playlists":
                context.DrawRectangle(Brushes.White, new Pen(Solid("#1C3C8C"), 1), new Rect(x + 3.5, y + 1.5, 10, 13));
                context.DrawGeometry(Solid("#2B58C8"), null, Poly(new Point(x + 6, y + 5), new Point(x + 11, y + 8), new Point(x + 6, y + 11)));
                break;
            case "queue":
                for (var i = 0; i < 4; i++)
                    context.FillRectangle(enabled ? Brushes.White : grey, new Rect(x + 4, y + 3 + i * 3, 10, 1.6));
                context.FillRectangle(enabled ? Solid("#7FD13B") : grey, new Rect(x + 1, y + 3, 2, 10));
                break;
            case "search":
                context.DrawEllipse(Solid("#DDE6FF"), new Pen(Solid("#1C3C8C"), 1.5), new Point(x + 10, y + 6), 4, 4);
                context.DrawLine(new Pen(Solid("#C0392B"), 3), new Point(x + 2.5, y + 13.5), new Point(x + 7, y + 9));
                break;
            case "add":
                context.FillRectangle(Solid("#3AAA35"), new Rect(x + 6, y + 2, 4, 12));
                context.FillRectangle(Solid("#3AAA35"), new Rect(x + 2, y + 6, 12, 4));
                context.FillRectangle(Solid("#8BE07F"), new Rect(x + 6, y + 2, 2, 12));
                break;
            case "delete":
                var cross = new Pen(enabled ? Solid("#E8EBF8") : grey, 2.2);
                context.DrawLine(cross, new Point(x + 3, y + 3), new Point(x + 13, y + 13));
                context.DrawLine(cross, new Point(x + 13, y + 3), new Point(x + 3, y + 13));
                break;
            case "up" or "down":
                var ink = enabled ? Solid("#4DA3FF") : grey;
                var up = key == "up";
                context.DrawGeometry(ink, null, Poly(new Point(x + 8, up ? y + 2 : y + 14), new Point(x + 14, y + 8), new Point(x + 2, y + 8)));
                context.FillRectangle(ink, new Rect(x + 6, up ? y + 8 : y + 2, 4, 6));
                break;
            case "albuminfo":
                context.DrawRectangle(enabled ? Brushes.White : grey, null, new Rect(x + 2, y + 2, 12, 12), 2, 2);
                context.DrawEllipse(Solid("#4154B0"), null, new Point(x + 8, y + 8), 3.5, 3.5);
                context.DrawEllipse(enabled ? Brushes.White : grey, null, new Point(x + 8, y + 8), 1.2, 1.2);
                break;
        }
    }

    /// <summary>The tree's node icons.</summary>
    private static void DrawLibraryNodeIcon(DrawingContext context, string key, Rect r)
    {
        double x = r.X, y = r.Y;
        switch (key)
        {
            case "nowplaying":
                context.DrawGeometry(Solid("#33CC33"), new Pen(Solid("#1E7A1E"), 1), Poly(new Point(x + 4, y + 2), new Point(x + 13, y + 7), new Point(x + 4, y + 12)));
                break;
            case "music":
                context.DrawEllipse(Solid("#9DB4F5"), new Pen(Solid("#3B55B5"), 1), new Point(x + 6, y + 11), 3, 2.2);
                context.DrawLine(new Pen(Solid("#C7D4FA"), 1.5), new Point(x + 8.5, y + 11), new Point(x + 8.5, y + 1));
                context.DrawLine(new Pen(Solid("#C7D4FA"), 1.5), new Point(x + 8.5, y + 1), new Point(x + 12.5, y + 4));
                break;
            case "video":
                context.FillRectangle(Solid("#C7D4FA"), new Rect(x + 1, y + 3, 14, 8));
                for (var i = 0; i < 5; i++)
                {
                    context.FillRectangle(Solid("#202C66"), new Rect(x + 2 + i * 3, y + 4, 1.5, 1.5));
                    context.FillRectangle(Solid("#202C66"), new Rect(x + 2 + i * 3, y + 8.5, 1.5, 1.5));
                }

                break;
            case "other":
                context.DrawRectangle(Solid("#E8ECF8"), new Pen(Solid("#8A99C8"), 1), new Rect(x + 3.5, y + 1.5, 9, 11));
                context.DrawEllipse(Solid("#8A99C8"), null, new Point(x + 8, y + 7), 2.5, 2.5);
                break;
            case "playlist":
                context.DrawRectangle(Brushes.White, new Pen(Solid("#7F9DB9"), 1), new Rect(x + 3.5, y + 0.5, 9, 12));
                context.DrawGeometry(Solid("#2B58C8"), null, Poly(new Point(x + 6, y + 3.5), new Point(x + 10.5, y + 6.5), new Point(x + 6, y + 9.5)));
                break;
            case "auto":
                context.DrawRectangle(Brushes.White, new Pen(Solid("#7F9DB9"), 1), new Rect(x + 3.5, y + 0.5, 9, 12));
                context.DrawEllipse(Solid("#33AA33"), null, new Point(x + 11, y + 10), 4, 4);
                context.DrawGeometry(Brushes.White, null, Poly(new Point(x + 9.5, y + 8), new Point(x + 13, y + 10), new Point(x + 9.5, y + 12)));
                break;
            case "radio":
                context.DrawRectangle(Solid("#9AA7C8"), new Pen(Solid("#5D6B95"), 1), new Rect(x + 1.5, y + 5.5, 13, 7), 2, 2);
                context.DrawEllipse(Solid("#2A355E"), null, new Point(x + 5, y + 9), 2, 2);
                context.DrawLine(new Pen(Solid("#C7D4FA"), 1), new Point(x + 4, y + 5), new Point(x + 12, y + 1));
                break;
            case "premium":
                context.DrawEllipse(Solid("#4DA3FF"), new Pen(Solid("#C7D4FA"), 1), new Point(x + 8, y + 7), 5.5, 5.5);
                context.DrawLine(new Pen(Solid("#3AAA35"), 2), new Point(x + 4, y + 6), new Point(x + 9, y + 9));
                break;
            case "search":
                context.DrawEllipse(Solid("#DDE6FF"), new Pen(Solid("#C7D4FA"), 1.2), new Point(x + 9, y + 5), 3.5, 3.5);
                context.DrawLine(new Pen(Solid("#C0392B"), 2.5), new Point(x + 3, y + 12), new Point(x + 6.5, y + 8));
                break;
        }
    }

    private static void DrawToolbar(DrawingContext context, SkinSession session, string titleText, IReadOnlyList<string> buttons,
        List<(double X, double Width)> bounds, int pressed, double w, double toolbarH, bool blueBand = false, bool glass = false, PaneColors? own = null)
    {
        var center = session.MediaCenter;
        IBrush Shade(string name, string fallback) =>
            new SolidColorBrush(Tinted(Color.Parse(SkinValue.ToText(center.GetMember(name)) is { Length: 7 } c ? c : fallback), glass));
        var dark = Shade("appColorDark", "#212741");
        var medium = Shade("appColorMedium", "#394472");
        var light = Shade("appColorLight", "#6071BF");
        if (own is not null)
            (dark, medium, light) = (own.Bar, own.Bar, new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)));
        FormattedText Text(string text, double maxWidth, bool bold = false, double size = 11) => new(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            UiTheme.Typeface(bold), size, Brushes.White)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var modern = AppSettings.Current.ModernToolbars && !blueBand;
        context.FillRectangle(blueBand ? new SolidColorBrush(Tinted(Color.Parse("#3D6BC9"), glass)) : modern ? dark : medium, new Rect(0, 0, w, toolbarH));
        context.DrawLine(new Pen(modern ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : blueBand ? new SolidColorBrush(Tinted(Color.Parse("#7B9BE8"), glass)) : light, 1), new Point(0, toolbarH - 0.5), new Point(w, toolbarH - 0.5));
        var title = Text(titleText, Math.Max(80, w * 0.4), bold: true, size: 12);
        if (modern)
        {
            // Flat Windows 11 command bar: semibold title, borderless text buttons, a soft fill only when pressed.
            title = new FormattedText(titleText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(UiTheme.Family, FontStyle.Normal, FontWeight.SemiBold), 13, Brushes.White)
            {
                MaxTextWidth = Math.Max(80, w * 0.4),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            context.DrawText(title, new Point(10, (toolbarH - title.Height) / 2));
            bounds.Clear();
            var mx = 10 + title.Width + 20;
            for (var i = 0; i < buttons.Count; i++)
            {
                var label = Text(buttons[i], 160);
                var bw = label.Width + 20;
                if (mx + bw > w - 4)
                    break;
                if (i == pressed)
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), null, new Rect(mx, 4, bw, toolbarH - 8), 4, 4);
                context.DrawText(label, new Point(mx + 10, (toolbarH - label.Height) / 2));
                bounds.Add((mx, bw));
                mx += bw + 2;
            }

            return;
        }

        context.DrawText(title, new Point(8, (toolbarH - title.Height) / 2));
        bounds.Clear();
        var x = 8 + title.Width + 16;
        for (var i = 0; i < buttons.Count; i++)
        {
            var label = Text(buttons[i], 160);
            var bw = label.Width + 14;
            if (x + bw > w - 4)
                break;
            var rect = new Rect(x + 0.5, 5.5, bw, toolbarH - 11);
            context.DrawRectangle(i == pressed ? dark : light, new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1), rect, 3, 3);
            context.DrawText(label, new Point(x + 7, (toolbarH - label.Height) / 2));
            bounds.Add((x, bw));
            x += bw + 6;
        }
    }
    private static void DrawListPane(DrawingContext context, Pardeh.Skinning.Runtime.Panes.ListPaneElement pane)
    {
        const double toolbarH = Pardeh.Skinning.Runtime.Panes.ListPaneElement.ToolbarHeight;
        const double headerH = Pardeh.Skinning.Runtime.Panes.ListPaneElement.HeaderHeight;
        double rowH = Pardeh.Skinning.Runtime.Panes.ListPaneElement.RowHeight;
        const double footerH = Pardeh.Skinning.Runtime.Panes.ListPaneElement.FooterHeight;
        var model = pane.Model;
        var center = pane.View.Session.MediaCenter;
        var glassPane = pane.GetBool("glass");
        IBrush Shade(string name, string fallback) =>
            new SolidColorBrush(Tinted(Color.Parse(SkinValue.ToText(center.GetMember(name)) is { Length: 7 } c ? c : fallback), glassPane));
        var dark = Shade("appColorDark", "#212741");
        var medium = Shade("appColorMedium", "#394472");
        var light = Shade("appColorLight", "#6071BF");

        double w = pane.Width, h = pane.Height, listW = pane.ListWidth, bodyBottom = h - footerH;
        FormattedText Text(string text, IBrush brush, double maxWidth, bool bold = false, double size = 11) => new(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            UiTheme.Typeface(bold), size, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        // Opt-in pale palette (LISTPANE light="true"): the WMP 10 look. Defaults keep the dark blue one.
        var pale = pane.GetBool("light");
        // Opt-in blue glass palette (LISTPANE glass="true", Pardeh Glass phone): deep blue body, glossy bar, glossy selected row.
        var glass = pane.GetBool("glass");
        // Opt-in colours of a look's own (the PC retro looks).
        var own = PaneColors.Of(pane);
        var rowInk = own?.Ink ?? (pale ? Solid("#1B2A55") : glass ? Solid(GlassPalette.Map("#D6E6FF")) : LibraryInk);
        var activeInk = own?.Accent ?? (pale ? Solid("#1B7A1B") : glass ? Brushes.White : LibraryPlaying);
        if (own is not null)
            (dark, light) = (own.Back, own.Selection);
        IBrush Vertical(params (string Color, double At)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            foreach (var (color, at) in stops)
                brush.GradientStops.Add(new GradientStop(GlassPalette.Parse(color), at)); // only the glass look asks for this gradient
            return brush;
        }

        context.FillRectangle(own?.Back ?? (pale ? Solid("#EDF3FD") : glass ? Vertical(("#1342B3", 0), ("#0A2A82", 1)) : LibraryBack), new Rect(0, 0, w, h));

        DrawToolbar(context, pane.View.Session, model.Title, model.Buttons, pane.ButtonBounds, pane.PressedButton, w, toolbarH, pale || glass, glass, own);
        if (glass)
            context.FillRectangle(Vertical(("#9AFFFFFF", 0), ("#00FFFFFF", 0.5), ("#40051850", 1)), new Rect(0, 0, w, toolbarH - 1));

        // Column headers.
        var top = toolbarH;
        context.FillRectangle(own?.Bar ?? (pale ? Solid("#EFEFEF") : glass ? Solid(GlassPalette.Map("#0F3799")) : LibraryHeader), new Rect(0, top, listW, headerH));
        context.DrawLine(glass ? new Pen(Solid(GlassPalette.Map("#4F89E6")), 1) : LibraryRule, new Point(0, top + headerH - 0.5), new Point(listW, top + headerH - 0.5));
        var totalWeight = model.Columns.Sum(c => c.Weight);
        var columnX = new List<(double X, double W)>();
        var cx = 0.0;
        foreach (var column in model.Columns)
        {
            var cw = listW * column.Weight / totalWeight;
            columnX.Add((cx, cw));
            var header = Text(column.Header, own?.BarInk ?? (glass ? Solid(GlassPalette.Map("#B9D4FF")) : LibraryHeaderInk), cw - 10);
            context.DrawText(header, new Point(column.AlignRight ? cx + cw - header.Width - 6 : cx + 6, top + 2));
            if (cx > 0)
                context.DrawLine(LibraryRule, new Point(cx - 0.5, top + 3), new Point(cx - 0.5, top + headerH - 3));
            cx += cw;
        }

        // Rows.
        var rows = model.Rows;
        var active = model.ActiveRow;
        var listTop = top + headerH;
        using (context.PushClip(new Rect(0, listTop, listW, bodyBottom - listTop)))
        {
            for (var r = 0; r < pane.VisibleRows + 1; r++)
            {
                var index = pane.Scroll + r;
                if (index >= rows.Count)
                    break;
                var y = listTop + r * rowH;
                if (glass)
                    context.DrawLine(new Pen(Solid(GlassPalette.Map("#2A57B5")), 1), new Point(8, y + rowH - 0.5), new Point(listW - 8, y + rowH - 0.5));
                if (index == pane.Selected)
                    context.DrawRectangle(own?.Selection ?? (glass ? Vertical(("#7CC4FF", 0), ("#3C93F6", 0.48), ("#1D6BE6", 0.5), ("#1A5FD8", 1)) : pale ? Solid("#316AC5") : LibrarySelection),glass ? new Pen(Solid(GlassPalette.Map("#9ED2FF")), 1) : null, glass ? new Rect(3, y + 0.5, listW - 6, rowH - 1) : new Rect(0, y, listW, rowH), glass ? 5 : 0, glass ? 5 : 0);
                var ink = index == active ? activeInk : (pale || glass) && own is null && index == pane.Selected ? Brushes.White : rowInk;
                for (var c = 0; c < columnX.Count && c < rows[index].Length; c++)
                {
                    var cell = Text(rows[index][c], ink, columnX[c].W - 10);
                    var cellX = model.Columns[c].AlignRight ? columnX[c].X + columnX[c].W - cell.Width - 6 : columnX[c].X + 6;
                    context.DrawText(cell, new Point(cellX, y + 1 + (rowH - 16) / 2));
                }
            }

            if (rows.Count == 0)
                context.DrawText(Text(model.Status, own?.Dim ?? (pale ? Solid("#1B3A8C") : new SolidColorBrush(Tinted(Color.Parse("#C9D3F0"), glass))), listW - 20), new Point(10, listTop + 10));
        }

        // Details panel: preview and description of the selected row.
        if (pane.ShowsDetails)
        {
            var panel = new Rect(listW, top, w - listW, h - top);
            context.FillRectangle(pale ? Solid("#DCE6F8") : dark, panel);
            context.DrawLine(new Pen(pale ? Solid("#5B86D8") : light, 1), new Point(listW + 0.5, top), new Point(listW + 0.5, h));
            var textTop = top + 10;
            if (model.Preview(pane.Selected < 0 ? active : pane.Selected) is { } preview)
            {
                var box = new Rect(panel.X + 10, top + 10, panel.Width - 20, Math.Max(40, (panel.Height - 20) * 0.62));
                var scale = Math.Min(2, Math.Min(box.Width / preview.LogicalWidth, box.Height / preview.LogicalHeight));
                var size = new Size(preview.LogicalWidth * scale, preview.LogicalHeight * scale);
                var at = new Rect(box.X + (box.Width - size.Width) / 2, box.Y + (box.Height - size.Height) / 2, size.Width, size.Height);
                using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    context.DrawImage(BitmapBridge.ToAvalonia(preview), at);
                textTop = box.Bottom + 8;
            }

            var lines = model.Details(pane.Selected < 0 ? active : pane.Selected).Split('\n');
            for (var i = 0; i < lines.Length && textTop + 14 < h; i++)
            {
                var line = Text(lines[i], own is not null ? (i == 0 ? own.Ink : own.Dim) : pale ? Solid(i == 0 ? "#1B2A55" : "#2B3F78") : i == 0 ? Brushes.White : new SolidColorBrush(Color.Parse("#C9D3F0")), panel.Width - 20, bold: i == 0);
                context.DrawText(line, new Point(panel.X + 10, textTop));
                textTop += 15;
            }
        }

        // Footer.
        context.FillRectangle(own?.Bar ?? (pale ? Solid("#DCE6F8") : glass ? Solid(GlassPalette.Map("#0B2D88")) : new SolidColorBrush(Color.Parse("#0E1430"))), new Rect(0, bodyBottom, listW, footerH));
        context.DrawText(Text(model.Status, own?.BarInk ?? rowInk, listW - 12), new Point(6, bodyBottom + 2));
    }

    /// <summary>
    /// The colours a skin gives its desktop Library and list panes (libBack, libBar, libBarInk, libInk, libDim, libSel,
    /// libAccent). Only a pane that says libBack has them.
    /// </summary>
    private sealed record PaneColors(IBrush Back, IBrush Bar, IBrush BarInk, IBrush Ink, IBrush Dim, IBrush Faint, IBrush Selection, IBrush Accent)
    {
        public static PaneColors? Of(Pardeh.Skinning.Runtime.Elements.SkinElement element)
        {
            if (element.GetString("libBack") is not { Length: > 0 })
                return null;
            Color Read(string name, string fallback) => Color.Parse(element.GetString(name) is { Length: > 0 } s ? s : fallback);
            IBrush Brush(string name, string fallback) => new SolidColorBrush(Read(name, fallback));
            var dim = Read("libDim", "#9D9D9D");
            return new PaneColors(Brush("libBack", "#1C1C1C"), Brush("libBar", "#202020"), Brush("libBarInk", "#FFFFFF"), Brush("libInk", "#FFFFFF"),
                new SolidColorBrush(dim), new SolidColorBrush(Color.FromArgb(80, dim.R, dim.G, dim.B)), Brush("libSel", "#3A3A3A"), Brush("libAccent", "#60CDFF"));
        }
    }

    private static void DrawDropDown(DrawingContext context, DropDownPlaylistElement dropDown, MediaPlayer player)
    {
        var rect = new Rect(0, 0, dropDown.Width, dropDown.Height);
        if (dropDown.BackgroundImage is { } frame)
        {
            NineSlice(context, frame, rect, 3);
        }
        else
        {
            context.FillRectangle(Brushes.White, rect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(127, 157, 185)), 1), rect.Deflate(0.5));
        }

        // The playlist icon: a small page with a play triangle (drawn; the original is an icon resource).
        var iconX = 5.0;
        var iconY = Math.Round((rect.Height - 14) / 2);
        if (dropDown.BackgroundImage is not null)
        {
            var page = new StreamGeometry();
            using (var g = page.Open())
            {
                g.BeginFigure(new Point(iconX + 0.5, iconY + 0.5), true);
                g.LineTo(new Point(iconX + 8.5, iconY + 0.5));
                g.LineTo(new Point(iconX + 11.5, iconY + 3.5));
                g.LineTo(new Point(iconX + 11.5, iconY + 13.5));
                g.LineTo(new Point(iconX + 0.5, iconY + 13.5));
                g.EndFigure(true);
            }

            context.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromRgb(196, 212, 240), 1) },
            }, new Pen(new SolidColorBrush(Color.FromRgb(88, 118, 178)), 1), page);
            var play = new StreamGeometry();
            using (var g = play.Open())
            {
                g.BeginFigure(new Point(iconX + 4, iconY + 4), true);
                g.LineTo(new Point(iconX + 8, iconY + 7));
                g.LineTo(new Point(iconX + 4, iconY + 10));
                g.EndFigure(true);
            }

            context.FillRectangle(new SolidColorBrush(Color.FromRgb(40, 80, 170)), new Rect(iconX + 2, iconY + 3, 7, 8));
            context.DrawGeometry(Brushes.White, null, play);
        }

        var textLeft = dropDown.BackgroundImage is null ? 4 : iconX + 18;
        var arrowWidth = dropDown.ArrowImage?.Width ?? 9;
        var name = player.CurrentPlaylist.Name is { Length: > 0 } n ? n : "Now Playing";
        // Text and arrow colour: foregroundColor (Pardeh's Fluent full mode), else black like the original.
        IBrush ink = SkinValue.ParseColor(dropDown.GetString("foregroundColor")) is { } rgb
            ? new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb))
            : Brushes.Black;
        var text = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            UiTheme.Typeface(), 11, ink)
        {
            MaxTextWidth = Math.Max(1, rect.Width - textLeft - arrowWidth - 10),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(text, new Point(textLeft, Math.Round((rect.Height - text.Height) / 2)));

        if (dropDown.ArrowImage is { } chevron)
        {
            Draw(context, chevron, rect.Width - chevron.LogicalWidth - 9, Math.Round((rect.Height - chevron.LogicalHeight) / 2));
            return;
        }

        var cx = rect.Width - 9;
        var cy = rect.Height / 2;
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(new Point(cx - 4, cy - 2), true);
            g.LineTo(new Point(cx + 4, cy - 2));
            g.LineTo(new Point(cx, cy + 2));
            g.EndFigure(true);
        }

        context.DrawGeometry(ink, null, arrow);
    }

    /// <summary>Stretches a framed image to <paramref name="area"/>, keeping its corners and edges <paramref name="edge"/> px wide.</summary>
    private static void NineSlice(DrawingContext context, SkinBitmap bitmap, Rect area, double edge)
    {
        var image = BitmapBridge.ToAvalonia(bitmap);
        using var pixelExact = context.PushRenderOptions(OptionsFor(bitmap));
        double sw = bitmap.LogicalWidth, sh = bitmap.LogicalHeight, s = bitmap.Scale;
        double[] sx = [0, edge, sw - edge, sw], sy = [0, edge, sh - edge, sh];
        double[] dx = [area.X, area.X + edge, area.Right - edge, area.Right], dy = [area.Y, area.Y + edge, area.Bottom - edge, area.Bottom];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var source = new Rect(sx[col] * s, sy[row] * s, (sx[col + 1] - sx[col]) * s, (sy[row + 1] - sy[row]) * s);
                var target = new Rect(dx[col], dy[row], dx[col + 1] - dx[col], dy[row + 1] - dy[row]);
                if (source.Width > 0 && source.Height > 0 && target.Width > 0 && target.Height > 0)
                    context.DrawImage(image, source, target);
            }
        }
    }

    private static Color ToColor(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
