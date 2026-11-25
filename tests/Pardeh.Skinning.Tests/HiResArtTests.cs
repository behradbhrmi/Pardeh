using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Wmp;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// High-resolution skin art: a bitmap that carries the density marker holds Scale pixels per skin unit, but is laid out,
/// hit tested, tiled and drawn in skin units, so a dense screen gets sharper art with the same layout.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class HiResArtTests
{
    private static readonly object Gate = new();
    private static bool _platformUp;

    private static void EnsurePlatform()
    {
        lock (Gate)
        {
            if (_platformUp)
                return;
            try
            {
                AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            }
            catch (InvalidOperationException)
            {
                // Another test class set the platform up first.
            }

            _platformUp = true;
        }
    }

    private static SkinBitmap Solid(int w, int h, int scale = 1)
    {
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (0, 0, 255, 255); // opaque red
        return new SkinBitmap(w, h, pixels, scale);
    }

    private static (SkinView View, SkinSession Session) Start(string wms, Dictionary<string, byte[]> art)
    {
        var files = new Dictionary<string, byte[]> { ["t.wms"] = Encoding.UTF8.GetBytes(wms) };
        foreach (var (name, data) in art)
            files[name] = data;
        var package = WmzLoader.FromFiles(files, "memory://t");
        var session = new SkinSession(package, new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader()), new FakeHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session.CurrentView!, session);
    }

    private static int PixelAt(byte[] bgra, int width, int x, int y) =>
        (bgra[(y * width + x) * 4 + 2] << 16) | (bgra[(y * width + x) * 4 + 1] << 8) | bgra[(y * width + x) * 4];

    [Fact]
    public void A_bitmap_has_scale_one_unless_told_otherwise_and_reports_skin_units()
    {
        var plain = Solid(12, 6);
        Assert.Equal(1, plain.Scale);
        Assert.Equal(12, plain.LogicalWidth);

        var dense = Solid(12, 6, 3);
        Assert.Equal(3, dense.Scale);
        Assert.Equal(12, dense.Width);          // pixels stay pixels
        Assert.Equal(4, dense.LogicalWidth);    // layout is in units
        Assert.Equal(2, dense.LogicalHeight);
        Assert.Same(dense.Pixels, dense.WithScale(2).Pixels);
        Assert.Equal(1, dense.WithScale(1).Scale);
    }

    [Fact]
    public void Copies_keep_the_density_and_positions_are_read_in_skin_units()
    {
        var pixels = new byte[12 * 6 * 4];
        var marked = (4 * 12 + 7) * 4; // pixel (7, 4): unit (2.33, 1.33)
        pixels[marked + 3] = 255;
        pixels[marked + 2] = 200;
        var dense = new SkinBitmap(12, 6, pixels, 3);

        Assert.Equal(255, dense.GetAlphaAt(2.5, 1.5));   // pixel (7, 4)
        Assert.Equal(0, dense.GetAlphaAt(0.5, 0.5));
        Assert.Equal(0, dense.GetAlphaAt(4.5, 0.5));     // outside: 4 units wide
        Assert.Equal(0xC80000, dense.GetRgbAt(2.5, 1.5));
        Assert.Equal(3, dense.WithColorKey(0x123456).Scale);
        Assert.Equal(3, dense.WithHueSaturation(30, 1).Scale);

        var stretched = dense.ResizedToUnits(8, 4);       // twice the units = 24 x 12 pixels
        Assert.Equal((24, 12, 3), (stretched.Width, stretched.Height, stretched.Scale));
        Assert.Equal((8.0, 4.0), (stretched.LogicalWidth, stretched.LogicalHeight));
    }

    [Fact]
    public void The_density_marker_round_trips_and_leaves_ordinary_pngs_alone()
    {
        EnsurePlatform();
        var dense = LunaArt.Png(10, 4, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 10, 4)), 3);
        var plain = LunaArt.Png(10, 4, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 10, 4)));

        Assert.Equal(3, PngDensity.Read(dense));
        Assert.Equal(1, PngDensity.Read(plain));
        Assert.Equal(1, PngDensity.Read([1, 2, 3, 4]));          // not a PNG
        Assert.Equal(1, PngDensity.Read(PngDensity.Mark(plain, 1)));

        var decoder = new AvaloniaImageDecoder();
        var decoded = decoder.Decode(dense)!;                    // other readers skip the private chunk
        Assert.Equal((30, 12), (decoded.Width, decoded.Height));
        Assert.Equal(1, decoded.Scale);                          // the cache, not the decoder, applies the density
        Assert.Equal((10, 4), (decoder.Decode(plain)!.Width, decoder.Decode(plain)!.Height));
    }

    [Fact]
    public void A_dense_button_image_is_laid_out_hit_tested_and_drawn_in_skin_units()
    {
        EnsurePlatform();
        // 20 x 10 units drawn at 3x (60 x 30 pixels); only the left half is opaque.
        var dense = LunaArt.Png(20, 10, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 10, 10)), 3);
        var (view, _) = Start("""<THEME><VIEW width="40" height="20" backgroundColor="#000000"><BUTTON id="b" left="10" top="5" image="b.png"/></VIEW></THEME>""",
            new() { ["b.png"] = dense });

        var button = view.FindById("b")!;
        Assert.Equal((20.0, 10.0), (button.Width, button.Height));
        Assert.True(button.HitTest(2, 5));      // opaque half (unit coordinates)
        Assert.False(button.HitTest(17, 5));    // transparent half

        var pixels = LunaArt.Render(40, 20, g => new SkinRenderer().Render(g, view));
        Assert.Equal(0xFF0000, PixelAt(pixels, 40, 12, 8));   // inside the opaque half, at its skin-unit position
        Assert.Equal(0x000000, PixelAt(pixels, 40, 22, 8));   // the transparent half shows the black view
        Assert.Equal(0x000000, PixelAt(pixels, 40, 8, 8));    // left of the button
        Assert.Equal(0x000000, PixelAt(pixels, 40, 12, 3));   // above it
    }

    [Fact]
    public void A_dense_image_renders_at_screen_density_with_unchanged_layout()
    {
        EnsurePlatform();
        var dense = LunaArt.Png(20, 10, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 20, 10)), 3);
        var (view, _) = Start("""<THEME><VIEW width="40" height="20" backgroundColor="#000000"><BUTTON id="b" left="10" top="5" image="b.png"/></VIEW></THEME>""",
            new() { ["b.png"] = dense });

        // Rendered at 3 pixels per unit (as the phone does), every pixel of the 60 x 30 button is the art's own.
        var pixels = LunaArt.Render(40, 20, g => new SkinRenderer().Render(g, view), 3);
        Assert.Equal(0xFF0000, PixelAt(pixels, 120, 30, 15));
        Assert.Equal(0xFF0000, PixelAt(pixels, 120, 30 + 59, 15 + 29));
        Assert.Equal(0x000000, PixelAt(pixels, 120, 29, 15));
        Assert.Equal(0x000000, PixelAt(pixels, 120, 30 + 60, 15));
    }

    [Fact]
    public void Tiling_and_slider_thumbs_use_skin_units()
    {
        EnsurePlatform();
        // A 2 x 4 unit tile at 3x repeated across 10 units; a slider whose 6 x 10 unit thumb is a 3x image.
        var tile = LunaArt.Png(2, 4, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 1, 4)), 3);
        var thumb = LunaArt.Png(6, 10, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 6, 10)), 3);
        var (view, _) = Start("""
            <THEME><VIEW width="40" height="20" backgroundColor="#000000">
              <SUBVIEW id="t" left="0" top="0" width="10" height="4" backgroundImage="tile.png" backgroundTiled="true"/>
              <SLIDER id="s" left="0" top="8" width="30" height="10" thumbImage="thumb.png" min="0" max="100" value="50"/>
            </VIEW></THEME>
            """, new() { ["tile.png"] = tile, ["thumb.png"] = thumb });

        var slider = (SliderElement)view.FindById("s")!;
        var rect = slider.ThumbRect;
        Assert.Equal((6.0, 10.0), (rect.W, rect.H));
        Assert.Equal(12.0, rect.X);                       // half of the 24 units of travel

        var subview = view.FindById("t")!;
        Assert.True(subview.HitTest(0.5, 1));             // the opaque left unit of the first tile
        Assert.False(subview.HitTest(1.5, 1));
        Assert.True(subview.HitTest(4.5, 1));             // and of the third tile

        // At the screen's own density (3 pixels per unit) the art's pixels land one to one.
        var pixels = LunaArt.Render(40, 20, g => new SkinRenderer().Render(g, view), 3);
        Assert.Equal(0xFF0000, PixelAt(pixels, 120, 4 * 3 + 1, 4));   // third tile's opaque unit
        Assert.Equal(0x000000, PixelAt(pixels, 120, 5 * 3 + 1, 4));   // its transparent unit
        Assert.Equal(0xFF0000, PixelAt(pixels, 120, 14 * 3 + 1, 12 * 3)); // the thumb, units 12..18
    }

    [Fact]
    public void The_window_mask_samples_dense_art_in_skin_units()
    {
        EnsurePlatform();
        var dense = LunaArt.Png(20, 10, g => g.FillRectangle(Brushes.Red, new Rect(0, 0, 10, 10)), 3);
        var (view, _) = Start("""<THEME><VIEW width="40" height="20"><BUTTON id="b" left="10" top="5" image="b.png"/></VIEW></THEME>""",
            new() { ["b.png"] = dense });

        var mask = SkinMask.Compute(view);
        Assert.True(mask[12, 8]);
        Assert.False(mask[25, 8]);
        Assert.False(mask[8, 8]);
        Assert.Equal(10 * 10, mask.Opaque.Count(o => o));
    }

    [Fact]
    public void Phone_skins_built_at_a_scale_carry_it_in_every_picture_and_keep_their_layout()
    {
        EnsurePlatform();
        var saved = PhoneSkin.ArtScale;
        try
        {
            PhoneSkin.ArtScale = 1;
            var one = PhoneSkin.Build("glass-phone");
            PhoneSkin.ArtScale = 3;
            var three = PhoneSkin.Build("glass-phone");

            Assert.Equal(1, PngDensity.Read(one.ReadFile("tb_close.png")!));
            Assert.Equal(3, PngDensity.Read(three.ReadFile("tb_close.png")!));
            var decoder = new AvaloniaImageDecoder();
            var a = decoder.Decode(one.ReadFile("play.png")!)!;
            var b = decoder.Decode(three.ReadFile("play.png")!)!;
            Assert.Equal((a.Width * 3, a.Height * 3), (b.Width, b.Height));

            // The view and its elements have the same size and positions at either scale.
            var (view1, _) = StartPackage(one);
            var (view3, _) = StartPackage(three);
            foreach (var id in new[] { "bPlay", "bPrev", "bNext", "bClose", "tabNow", "svPill" })
            {
                var e1 = view1.FindById(id)!;
                var e3 = view3.FindById(id)!;
                Assert.Equal((e1.Left, e1.Top, e1.Width, e1.Height), (e3.Left, e3.Top, e3.Width, e3.Height));
            }
        }
        finally
        {
            PhoneSkin.ArtScale = saved;
        }
    }

    private static (SkinView View, SkinSession Session) StartPackage(SkinPackage package)
    {
        var session = new SkinSession(package, new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader()), new FakeHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session.CurrentView!, session);
    }
}
