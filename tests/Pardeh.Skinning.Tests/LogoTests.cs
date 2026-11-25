using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Pardeh.UI.FullMode;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// Pardeh's logo (<see cref="PardehLogoArt"/>): it renders at every size and art density with a distinct silhouette, the Android
/// drawables and the .ico are what the art generates, and the preview / export hooks (environment variables) for a look.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class LogoTests
{
    private static readonly object Gate = new();
    private static bool _platformUp;

    internal static void EnsurePlatform()
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
                // Another test class already set the platform up.
            }

            _platformUp = true;
        }
    }

    /// <summary>The logo at <paramref name="size"/> logical pixels, <paramref name="scale"/> pixels per unit: premultiplied BGRA.</summary>
    private static byte[] Logo(int size, int scale = 1) => LunaArt.Render(size, size, g => PardehLogoArt.Full.Draw(g, new Rect(0, 0, size, size)), scale);

    private static (int R, int G, int B, int A) Px(byte[] pixels, int side, double fx, double fy) => Px(pixels, side, side, fx, fy);

    /// <summary>The pixel at a fraction of the picture, un-premultiplied (RGBA).</summary>
    private static (int R, int G, int B, int A) Px(byte[] pixels, int width, int height, double fx, double fy)
    {
        var i = ((int)(fy * height) * width + (int)(fx * width)) * 4;
        var a = pixels[i + 3];
        if (a == 0)
            return (0, 0, 0, 0);
        return (Math.Min(255, pixels[i + 2] * 255 / a), Math.Min(255, pixels[i + 1] * 255 / a), Math.Min(255, pixels[i] * 255 / a), a);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(96)]
    [InlineData(512)]
    public void Logo_renders_a_round_orb_with_alpha_at_every_size(int size)
    {
        EnsurePlatform();
        var pixels = Logo(size);
        var opaque = 0;
        var clear = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 200)
                opaque++;
            else if (pixels[i] == 0)
                clear++;
        }

        var total = size * size;
        // A round orb in a square: a good share is solid, the corners and the ground around it stay clear.
        Assert.InRange(opaque / (double)total, 0.45, 0.85);
        Assert.True(clear / (double)total > 0.1, $"{size}px: only {clear} clear pixels");
        Assert.Equal(0, Px(pixels, size, 0.01, 0.01).A);
        Assert.Equal(0, Px(pixels, size, 0.99, 0.01).A);
        Assert.True(Px(pixels, size, 0.5, 0.45).A > 200);
    }

    /// <summary>The logo's own look: a blue glass orb with a bright (white-ish) play triangle in the middle, at every size.</summary>
    [Theory]
    [InlineData(24)]
    [InlineData(48)]
    [InlineData(96)]
    [InlineData(512)]
    public void Logo_shows_a_blue_glass_orb_with_a_bright_triangle(int size)
    {
        EnsurePlatform();
        var pixels = Logo(size);
        var blue = 0;
        var light = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var (r, g, b, a) = Px(pixels, size, (x + 0.5) / size, (y + 0.5) / size);
                if (a < 200)
                    continue;
                if (b > 120 && b > r + 50)
                    blue++;
                else if (r > 190 && g > 200 && b > 220)
                    light++;
            }
        }

        var share = (double)size * size;
        Assert.True(blue / share > 0.25, $"{size}px blue {blue / share:P1}");
        Assert.True(light / share > 0.05, $"{size}px light {light / share:P1}");

        // The triangle is light, the glass around it (left of it, inside the rim) is blue.
        var tri = Px(pixels, size, 0.5, 0.5);
        Assert.True(tri.R > 170 && tri.G > 190 && tri.B > 220, $"{size}px: the triangle is {tri}");
        var glass = Px(pixels, size, 0.24, 0.56);
        Assert.True(glass.B > glass.R + 40, $"{size}px: the glass is {glass}");
    }

    /// <summary>Hi-res art: the same picture at 2x and 4x density, with the same coverage and no hard-edged leftovers.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Logo_is_the_same_picture_in_any_art_density(int scale)
    {
        EnsurePlatform();
        var low = Logo(48);
        var high = Logo(48, scale);
        Assert.Equal(48 * scale * 48 * scale * 4, high.Length);

        double Coverage(byte[] p) => Enumerable.Range(0, p.Length / 4).Sum(i => p[i * 4 + 3]) / 255.0 / (p.Length / 4);
        Assert.InRange(Coverage(high) / Coverage(low), 0.97, 1.03);

        // Downsampling the dense picture lands on the 1x one, so detail is only added, not moved.
        var side = 48 * scale;
        long diff = 0;
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 48; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0;
                    for (var dy = 0; dy < scale; dy++)
                        for (var dx = 0; dx < scale; dx++)
                            sum += high[((y * scale + dy) * side + x * scale + dx) * 4 + c];
                    diff += Math.Abs(sum / (scale * scale) - low[(y * 48 + x) * 4 + c]);
                }
            }
        }

        Assert.True(diff / (48.0 * 48 * 3) < 6, $"mean difference {diff / (48.0 * 48 * 3):F1}");
    }

    [Fact]
    public void Logo_is_not_square_bound()
    {
        EnsurePlatform();
        // A non-square target keeps the logo round and centred.
        var pixels = LunaArt.Render(60, 30, g => PardehLogoArt.Full.Draw(g, new Rect(0, 0, 60, 30)));
        Assert.True(Px(pixels, 60, 30, 0.5, 0.45).A > 200);
        Assert.Equal(0, Px(pixels, 60, 30, 0.05, 0.5).A);
        Assert.Equal(0, Px(pixels, 60, 30, 0.95, 0.5).A);
    }

    // ---- the picture logos ---------------------------------------------------------------

    private static (int R, int G, int B, int A) MasterPx(LogoImage m, double fx, double fy)
    {
        var i = ((int)(fy * m.Size) * m.Size + (int)(fx * m.Size)) * 4;
        var a = m.Pixels[i + 3];
        return a == 0 ? (0, 0, 0, 0) : (Math.Min(255, m.Pixels[i + 2] * 255 / a), Math.Min(255, m.Pixels[i + 1] * 255 / a), Math.Min(255, m.Pixels[i] * 255 / a), a);
    }

    [Theory]
    [InlineData(LogoVariant.Curtain)]
    public void Master_is_a_clean_transparent_square(LogoVariant variant)
    {
        EnsurePlatform();
        var m = LogoExport.Master(variant);
        Assert.Equal(m.Size, (int)Math.Sqrt(m.Pixels.Length / 4));
        Assert.True(m.Size >= 768, "the master is at least 768 px");
        // Transparent corners (no black box; the tile runs close to the edges), a solid middle, and every pixel valid premultiplied (colour <= alpha).
        foreach (var (fx, fy) in new[] { (0.0, 0.0), (0.999, 0.0), (0.0, 0.999), (0.999, 0.999) })
            Assert.True(MasterPx(m, fx, fy).A < 4, $"{variant}: {fx},{fy} is not transparent");
        long opaque = 0;
        for (var i = 0; i < m.Pixels.Length; i += 4)
        {
            Assert.True(m.Pixels[i] <= m.Pixels[i + 3] && m.Pixels[i + 1] <= m.Pixels[i + 3] && m.Pixels[i + 2] <= m.Pixels[i + 3], $"{variant}: invalid premultiplied pixel at {i / 4}");
            if (m.Pixels[i + 3] == 255)
                opaque++;
        }

        Assert.InRange(opaque / (double)(m.Size * m.Size), 0.5, 0.99);
    }

    /// <summary>Every picture draws crisp and complete at every size, with alpha, through the code the app uses.</summary>
    [Theory]
    [InlineData(LogoVariant.Curtain, 16)]
    [InlineData(LogoVariant.Curtain, 24)]
    [InlineData(LogoVariant.Curtain, 48)]
    [InlineData(LogoVariant.Curtain, 96)]
    [InlineData(LogoVariant.Curtain, 512)]
    public void Picture_logo_draws_with_alpha_at_every_size(LogoVariant variant, int size)
    {
        EnsurePlatform();
        var logo = LogoImage.Get(variant)!;
        var pixels = LunaArt.Render(size, size, g => logo.Draw(g, new Rect(0, 0, size, size)));
        var opaque = 0;
        var clear = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 200)
                opaque++;
            else if (pixels[i] == 0)
                clear++;
        }

        Assert.InRange(opaque / (double)(size * size), 0.5, 0.99);
        Assert.True(clear / (double)(size * size) > 0.01, $"{variant} {size}px: only {clear} clear pixels");
        Assert.True(Px(pixels, size, 0.01, 0.01).A <= 4, $"corner alpha {Px(pixels, size, 0.01, 0.01).A}");
        Assert.True(Px(pixels, size, 0.99, 0.99).A <= 4, $"corner alpha {Px(pixels, size, 0.99, 0.99).A}");
    }

    [Fact]
    public void Pardeh_logo_draws_the_default_picture_and_keeps_its_colours_small()
    {
        EnsurePlatform();
        Assert.True(PardehLogoArt.IsImage(PardehLogoArt.Default));
        foreach (var size in new[] { 22, 30, 76 })
        {
            var pixels = LunaArt.Render(size, size, g => PardehLogo.Draw(g, new Rect(0, 0, size, size)));
            var white = 0;
            var cool = 0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var (r, gr, b, a) = Px(pixels, size, (x + 0.5) / size, (y + 0.5) / size);
                    if (a < 200)
                        continue;
                    if (r > 200 && gr > 200 && b > 200)
                        white++;
                    else if (b > 90 && b > r + 40)
                        cool++;
                }
            }

            Assert.True(white >= Math.Max(2, size * size / 40), $"{size}px: {white} white pixels (the play triangle)");
            Assert.True(cool > size * size / 2, $"{size}px: only {cool} blue pixels");
        }
    }

    /// <summary>Art density: the same picture at 1x, 2x and 4x (a skin baked for a hi-res screen), the same coverage, the 2x / 4x only sharper.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Pardeh_logo_is_the_same_picture_in_any_art_density(int scale)
    {
        EnsurePlatform();
        var low = LunaArt.Render(48, 48, g => PardehLogo.Draw(g, new Rect(0, 0, 48, 48)));
        var high = LunaArt.Render(48, 48, g => PardehLogo.Draw(g, new Rect(0, 0, 48, 48)), scale);
        double Coverage(byte[] p) => Enumerable.Range(0, p.Length / 4).Sum(i => p[i * 4 + 3]) / 255.0 / (p.Length / 4);
        Assert.InRange(Coverage(high) / Coverage(low), 0.97, 1.03);
        var side = 48 * scale;
        long diff = 0;
        for (var y = 0; y < 48; y++)
            for (var x = 0; x < 48; x++)
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0;
                    for (var dy = 0; dy < scale; dy++)
                        for (var dx = 0; dx < scale; dx++)
                            sum += high[((y * scale + dy) * side + x * scale + dx) * 4 + c];
                    diff += Math.Abs(sum / (scale * scale) - low[(y * 48 + x) * 4 + c]);
                }

        Assert.True(diff / (48.0 * 48 * 3) < 8, $"mean difference {diff / (48.0 * 48 * 3):F1}");
    }

    /// <summary>The resampler is a real low-pass: fine scan lines shrink to a near-flat tone (no moire) and flat colour and alpha are kept.</summary>
    [Fact]
    public void Resampler_averages_fine_lines_and_keeps_flat_colour()
    {
        const int N = 480;
        var src = new float[N * N * 4];
        for (var y = 0; y < N; y++)
            for (var x = 0; x < N; x++)
            {
                var line = y % 6 < 3 ? 1f : 0.3f; // 6 px period, like the scan lines of the neon artwork at master size
                var o = (y * N + x) * 4;
                src[o] = line * 0.5f;
                src[o + 1] = line * 0.8f;
                src[o + 2] = line;
                src[o + 3] = 1f;
            }

        foreach (var size in new[] { 96, 60, 48, 24 })
        {
            var dst = LogoResampler.Resize(src, N, N, size, size);
            LogoResampler.Clamp(dst);
            float min = 9, max = -9;
            for (var y = size / 4; y < size * 3 / 4; y++)
            {
                var v = dst[(y * size + size / 2) * 4 + 2];
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }

            Assert.True(max - min < 0.08, $"{size}px: the lines left a ripple of {max - min:F3}");
            Assert.InRange(dst[(size / 2 * size + size / 2) * 4 + 3], 0.999f, 1.001f);
        }

        var flat = Enumerable.Repeat(0.4f, N * N * 4).ToArray();
        var small = LogoResampler.Resize(flat, N, N, 37, 37);
        Assert.All(small, v => Assert.InRange(v, 0.399f, 0.401f));
    }

    // ---- Android picture files, themed icon, status-bar icon -----------------------------------------------------------

    /// <summary>
    /// (Replaces "Android_drawables_are_what_the_art_generates", which compared generated vector XML text.) The picture files in the
    /// Android resources are what the default variant's master generates: right names, right sizes, real alpha, real content, the same
    /// pixels as a fresh export (so a variant switch without running scripts/export-logo-image.ps1 fails here), and the old vector
    /// drawables with the same names are gone (a .png and an .xml of one name would clash at build time).
    /// </summary>
    [Fact]
    public void Android_logo_files_are_what_the_master_generates()
    {
        if (!Directory.Exists(Path.Combine(LogoExport.RepoRoot(), "src", "Pardeh.Android", "Resources")))
            return; // the test binary was built elsewhere
        EnsurePlatform();
        var resources = Path.Combine(LogoExport.RepoRoot(), "src", "Pardeh.Android", "Resources");
        foreach (var (name, expected) in LogoExport.AndroidFiles(PardehLogoArt.Default))
        {
            var path = Path.Combine(resources, name.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{name} is missing (run scripts/export-logo-image.ps1)");
            var actual = File.ReadAllBytes(path);
            if (name.EndsWith(".xml", StringComparison.Ordinal))
            {
                Assert.Equal(Encoding.UTF8.GetString(expected).ReplaceLineEndings("\n"), Encoding.UTF8.GetString(actual).ReplaceLineEndings("\n"));
                continue;
            }

            var (w, h, pixels) = LogoExport.Decode(actual);
            var (ew, eh, epixels) = LogoExport.Decode(expected);
            Assert.True(w == ew && h == eh, $"{name}: {w}x{h}, expected {ew}x{eh}");
            long diff = 0;
            var opaque = 0;
            var clear = 0;
            for (var i = 0; i < pixels.Length; i++)
                diff += Math.Abs(pixels[i] - epixels[i]);
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 200)
                    opaque++;
                else if (pixels[i] == 0)
                    clear++;
            }

            Assert.True(diff / (double)pixels.Length < 0.5, $"{name} differs from the master's export (mean {diff / (double)pixels.Length:F2}): run scripts/export-logo-image.ps1");
            Assert.True(opaque > w * h * 0.04, $"{name} is almost empty");
            Assert.True(clear > w * h * 0.04, $"{name} has no transparent area");
        }

        foreach (var gone in LogoExport.ObsoleteAndroidFiles)
            Assert.False(File.Exists(Path.Combine(resources, gone.Replace('/', Path.DirectorySeparatorChar))), $"{gone} should be gone (it was replaced by a picture of the same name)");
        // The expected sizes: 512 logo, 432 layers (108 dp x 4), the status icon 24 dp at five densities.
        var sizes = LogoExport.AndroidFiles(PardehLogoArt.Default).Where(p => p.Key.EndsWith(".png", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => LogoExport.Decode(p.Value).Width);
        Assert.Equal(512, sizes["drawable-nodpi/pardeh_logo.png"]);
        Assert.Equal(432, sizes["drawable-nodpi/ic_launcher_foreground.png"]);
        Assert.Equal(432, sizes["drawable-nodpi/ic_launcher_monochrome.png"]);
        Assert.Equal([24, 36, 48, 72, 96], LogoExport.StatusSizes.Select(s => sizes[s.Folder + "/ic_stat_pardeh.png"]));
    }

    /// <summary>The foreground and the themed layer keep every solid pixel of the mark inside the 66 dp safe zone, are not tiny, and are centred; all variants.</summary>
    [Fact]
    public void Launcher_layers_stay_in_the_safe_zone()
    {
        EnsurePlatform();
        foreach (var variant in LogoExport.ImageVariants)
        {
            foreach (var (name, layer) in new[] { ("foreground", LogoExport.Foreground(variant)), ("monochrome", LogoExport.Monochrome(variant)) })
            {
                const int S = LogoExport.LayerPx; // 4 px per dp
                double min = S, max = 0, sumX = 0, sumY = 0, weight = 0;
                for (var y = 0; y < S; y++)
                {
                    for (var x = 0; x < S; x++)
                    {
                        var o = (y * S + x) * 4;
                        var a = layer[o + 3];
                        if (a < 128)
                            continue;
                        // The curtain's foreground carries its navy tile too: the mark is what is bright on it.
                        if (variant == LogoVariant.Curtain && name == "foreground" && Math.Max(layer[o], layer[o + 1]) * 255 / a < 140)
                            continue;
                        // Everything solid is inside the 66 dp safe circle (the mask shows 72 dp, circle masks cut at 36).
                        var d = Math.Sqrt(Math.Pow(x + 0.5 - S / 2.0, 2) + Math.Pow(y + 0.5 - S / 2.0, 2)) / 4;
                        Assert.True(d <= 33, $"{variant} {name}: a solid pixel at {d:F1} dp from the centre");
                        min = Math.Min(min, Math.Min(x, y));
                        max = Math.Max(max, Math.Max(x, y));
                        sumX += x;
                        sumY += y;
                        weight++;
                    }
                }

                Assert.True(max - min > 46 * 4,$"{variant} {name} is only {(max - min) / 4.0:F0} dp wide");
                Assert.InRange(sumX / weight, S * 0.4, S * 0.6);
                Assert.InRange(sumY / weight, S * 0.4, S * 0.6);
            }
        }
    }

    /// <summary>
    /// The status-bar icon is a white silhouette (the system tints it) at every density: only white with alpha, a good share solid,
    /// the mark centred, and a clear hole / gap inside the mark so it reads as a play symbol at 24 dp.
    /// </summary>
    [Fact]
    public void Status_icon_is_a_white_silhouette_that_reads_at_24_dp()
    {
        EnsurePlatform();
        foreach (var variant in LogoExport.ImageVariants)
        {
            foreach (var (_, px) in LogoExport.StatusSizes)
            {
                var pixels = LogoExport.Status(variant, px);
                var solid = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] > 128)
                        solid++;
                    // Only white: the premultiplied colour equals the alpha.
                    Assert.True(Math.Abs(pixels[i] - pixels[i + 3]) < 3 && Math.Abs(pixels[i + 1] - pixels[i + 3]) < 3 && Math.Abs(pixels[i + 2] - pixels[i + 3]) < 3, $"{variant} {px}px: a coloured pixel");
                }

                Assert.InRange(solid / (double)(px * px), 0.18, 0.65);
                Assert.Equal(0, Px(pixels, px, 0.01, 0.01).A);
                Assert.Equal(0, Px(pixels, px, 0.99, 0.99).A);
                // Some clear air inside the mark (between the ribbons and the triangle).
                var clearInside = 0;
                for (var y = 0; y < px; y++)
                    for (var x = 0; x < px; x++)
                        if (Math.Sqrt(Math.Pow(x + 0.5 - px / 2.0, 2) + Math.Pow(y + 0.5 - px / 2.0, 2)) < px * 0.38 && pixels[(y * px + x) * 4 + 3] < 60)
                            clearInside++;
                Assert.True(clearInside >= 2, $"{variant} {px}px: no hole or gap in the middle");
            }
        }
    }

    [Fact]
    public void Exported_xml_draws_like_the_art()
    {
        EnsurePlatform();
        // The vector designs stay exportable: the Android reader (groups, clip, gradients) and the art model agree on the full-colour vector logo.
        const int S = 96;
        var xml = LogoExport.VectorDrawables()["pardeh_logo.xml"];
        var fromXml = LunaArt.Render(S, S, g => LogoExport.DrawVector(g, xml, new Rect(0, 0, S, S)));
        var fromArt = LunaArt.Render(S, S, g => PardehLogoArt.Full.Draw(g, new Rect(0, 0, S, S)));
        long diff = 0;
        for (var i = 0; i < fromXml.Length; i++)
            diff += Math.Abs(fromXml[i] - fromArt[i]);
        Assert.True(diff / (double)fromXml.Length < 1.5, $"mean difference {diff / (double)fromXml.Length:F2}");
    }

    // ---- Windows icon ------------------------------------------------------------------------------------------------

    [Fact]
    public void Ico_holds_every_size()
    {
        EnsurePlatform();
        var ico = LogoExport.Ico(PardehLogoArt.Default);
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.Equal(LogoExport.IcoSizes.Length, BitConverter.ToUInt16(ico, 4));
        for (var i = 0; i < LogoExport.IcoSizes.Length; i++)
        {
            var entry = 6 + i * 16;
            var width = ico[entry] == 0 ? 256 : ico[entry];
            Assert.Equal(LogoExport.IcoSizes[i], width);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            Assert.Equal(0x89, ico[offset]); // a PNG
            Assert.Equal((byte)'P', ico[offset + 1]);
        }

        var shipped = Path.Combine(LogoExport.RepoRoot(), "src", "Pardeh.App", "pardeh.ico");
        if (File.Exists(shipped))
        {
            var bytes = File.ReadAllBytes(shipped);
            Assert.Equal(LogoExport.IcoSizes.Length, BitConverter.ToUInt16(bytes, 4));
        }
    }

    // ---- hooks for a look (environment variables) --------------------------------------------------------------------

    /// <summary>
    /// <c>PARDEH_LOGO_EXPORT=&lt;repo root&gt;</c> rewrites everything that is a copy of the default variant's master picture: the Android
    /// launcher layers, notification large icon and status icons, the tile, <c>src/Pardeh.App/pardeh.ico</c>, <c>docs/img/pardeh-logo-512.png</c>
    /// (<c>scripts/export-logo-image.ps1</c> does it for you).
    /// </summary>
    [Fact]
    public void Export_logo_files()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_LOGO_EXPORT") is not { Length: > 0 } root)
            return;
        EnsurePlatform();
        var resources = Path.Combine(root, "src", "Pardeh.Android", "Resources");
        foreach (var gone in LogoExport.ObsoleteAndroidFiles)
            File.Delete(Path.Combine(resources, gone.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var (name, bytes) in LogoExport.AndroidFiles(PardehLogoArt.Default))
        {
            var path = Path.Combine(resources, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        File.WriteAllBytes(Path.Combine(root, "src", "Pardeh.App", "pardeh.ico"), LogoExport.Ico(PardehLogoArt.Default));
        foreach (var (name, px) in LogoExport.WidgetImages)
            File.WriteAllBytes(Path.Combine(root, "src", "Pardeh.Widgets", "Images", name), LogoExport.Png(LogoExport.Logo(PardehLogoArt.Default, px), px));
        Directory.CreateDirectory(Path.Combine(root, "docs", "img"));
        File.WriteAllBytes(Path.Combine(root, "docs", "img", "pardeh-logo-512.png"), LogoExport.Png(LogoExport.Logo(PardehLogoArt.Default, 512), 512));
    }

    /// <summary>
    /// <c>PARDEH_LOGO_SHEET=&lt;folder&gt;</c> writes <c>logo.png</c> (sizes on dark, light and blue), <c>launcher.png</c> (adaptive icon
    /// under circle, squircle and rounded-square masks, and the themed icon) and <c>notification.png</c> (status-bar icon on a shade).
    /// </summary>
    [Fact]
    public void Preview_sheets()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_LOGO_SHEET") is not { Length: > 0 } folder)
            return;
        EnsurePlatform();
        Directory.CreateDirectory(folder);
        var variant = PardehLogoArt.Default;
        File.WriteAllBytes(Path.Combine(folder, "variants.png"), VariantsSheet());

        // The default variant at sizes on three backgrounds, drawn through PardehLogo.Draw (the code the app uses).
        {
            var bgs = new[] { "#0E1020", "#F3F6FB", "#0E3599" };
            const int PanelW = 400, H = 200;
            var pixels = LunaArt.Render(PanelW * 3, H, g =>
            {
                for (var b = 0; b < 3; b++)
                {
                    g.FillRectangle(new SolidColorBrush(Color.Parse(bgs[b])), new Rect(b * PanelW, 0, PanelW, H));
                    var x = b * PanelW + 10.0;
                    foreach (var s in new[] { 170, 96, 48, 24 })
                    {
                        PardehLogo.Draw(g, new Rect(x, 10, s, s));
                        x += s + 12;
                    }
                }
            });
            File.WriteAllBytes(Path.Combine(folder, "logo.png"), LunaArt.Png(pixels, PanelW * 3 * 4, 0, 0, PanelW * 3, H));
        }

        // Launcher: icon px sizes 48 / 72 / 96 / 192 under three masks, on a wallpaper-like gradient; then themed.
        {
            var sizes = new[] { 48, 72, 96, 192 };
            var w = 20 + sizes.Sum(s => s + 20) * 3;
            const int H = 480;
            var pixels = LunaArt.Render(w, H, g =>
            {
                g.FillRectangle(LunaArt.Vertical((Color.Parse("#2B3A55"), 0), (Color.Parse("#6B4E71"), 1)), new Rect(0, 0, w, H));
                var x = 20.0;
                for (var mask = 0; mask < 3; mask++)
                {
                    foreach (var s in sizes)
                    {
                        LauncherIcon(g, variant, new Rect(x, 20, s, s), mask, themed: false);
                        LauncherIcon(g, variant, new Rect(x, 20 + 250, s, s), mask, themed: true);
                        x += s + 20;
                    }
                }
            });
            File.WriteAllBytes(Path.Combine(folder, "launcher.png"), LunaArt.Png(pixels, w * 4, 0, 0, w, H));
        }

        // Notification small icon at 1x / 2x / 3x (24 dp) on a dark and a light shade, plain and magnified.
        {
            const int W = 560, H = 190;
            var pixels = LunaArt.Render(W, H, g =>
            {
                g.FillRectangle(new SolidColorBrush(Color.Parse("#26262B")), new Rect(0, 0, W / 2, H));
                g.FillRectangle(new SolidColorBrush(Color.Parse("#E9E6EE")), new Rect(W / 2, 0, W / 2, H));
                var x = 20.0;
                foreach (var s in new[] { 24, 48, 72, 144 })
                {
                    StatusIcon(g, variant, new Rect(x, 20, s, s), Color.Parse("#FFFFFF"));
                    StatusIcon(g, variant, new Rect(W / 2 + x, 20, s, s), Color.Parse("#3B3A45"));
                    x += s + 16;
                }
            });
            File.WriteAllBytes(Path.Combine(folder, "notification.png"), LunaArt.Png(pixels, W * 4, 0, 0, W, H));

            // The real 24 x 24 px icon, magnified 8x without smoothing (what the pixels really are).
            var small = LunaArt.Render(72, 28, g =>
            {
                g.FillRectangle(new SolidColorBrush(Color.Parse("#26262B")), new Rect(0, 0, 36, 28));
                g.FillRectangle(new SolidColorBrush(Color.Parse("#E9E6EE")), new Rect(36, 0, 36, 28));
                StatusIcon(g, variant, new Rect(6, 2, 24, 24), Color.Parse("#FFFFFF"));
                StatusIcon(g, variant, new Rect(42, 2, 24, 24), Color.Parse("#3B3A45"));
            });
            const int Z = 8;
            var big = new byte[72 * Z * 28 * Z * 4];
            for (var y = 0; y < 28 * Z; y++)
                for (var x = 0; x < 72 * Z; x++)
                    Array.Copy(small, ((y / Z) * 72 + x / Z) * 4, big, (y * 72 * Z + x) * 4, 4);
            File.WriteAllBytes(Path.Combine(folder, "notification_zoom.png"), LunaArt.Png(big, 72 * Z * 4, 0, 0, 72 * Z, 28 * Z));
        }
    }

    /// <summary>The status icon tinted like the system does: the alpha of the white silhouette in <paramref name="tint"/>.</summary>
    private static void StatusIcon(DrawingContext g, LogoVariant variant, Rect r, Color tint)
    {
        var bitmap = LogoExport.ToBitmap(LogoExport.Status(variant, (int)r.Width), (int)r.Width);
        // Tint: the silhouette is an opacity mask (only its alpha counts), as when the system tints the icon.
        using (g.PushOpacityMask(new ImageBrush(bitmap) { Stretch = Stretch.Fill }, r))
            g.FillRectangle(new SolidColorBrush(tint), r);
    }

    /// <summary>
    /// The contact sheet of every picture variant (one column each, the default marked): the logo at 160 px on dark and light, sizes
    /// 96 / 48 / 24 on dark, light and blue, the launcher icon (96 px) under a circle, squircle and rounded square, the themed icon
    /// and the status icon at 24 / 72 px.
    /// </summary>
    private static byte[] VariantsSheet()
    {
        var variants = LogoExport.ImageVariants.ToArray();
        const int Col = 340, W0 = 10;
        var width = variants.Length * Col + W0;
        const int H = 760;
        var pixels = LunaArt.Render(width, H, g =>
        {
            g.FillRectangle(new SolidColorBrush(Color.Parse("#0E1020")), new Rect(0, 0, width, H));
            for (var i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                var logo = LogoImage.Get(v)!;
                var x0 = W0 + i * Col;
                g.FillRectangle(new SolidColorBrush(Color.Parse("#F3F6FB")), new Rect(x0 + Col / 2 - 5, 0, Col / 2 + 5, 190));
                logo.Draw(g, new Rect(x0 + 10, 10, 160, 160));
                logo.Draw(g, new Rect(x0 + Col / 2 + 5, 10, 160, 160));
                var bands = new[] { ("#0E1020", 200), ("#F3F6FB", 290), ("#0E3599", 380) };
                foreach (var (bg, y) in bands)
                {
                    g.FillRectangle(new SolidColorBrush(Color.Parse(bg)), new Rect(x0, y, Col - 10, 86));
                    var x = x0 + 10.0;
                    foreach (var s in new[] { 72, 48, 32, 24 })
                    {
                        logo.Draw(g, new Rect(x, y + 6, s, s));
                        x += s + 14;
                    }
                }

                // The launcher icon under three masks, then the themed one and the status icons.
                g.FillRectangle(LunaArt.Vertical((Color.Parse("#2B3A55"), 0), (Color.Parse("#6B4E71"), 1)), new Rect(x0, 478, Col - 10, 270));
                var lx = x0 + 10.0;
                for (var mask = 0; mask < 3; mask++)
                {
                    LauncherIcon(g, v, new Rect(lx, 490, 96, 96), mask, themed: false);
                    lx += 106;
                }

                var tx = x0 + 10.0;
                LauncherIcon(g, v, new Rect(tx, 610, 72, 72), 0, themed: true);
                tx += 90;
                g.FillRectangle(new SolidColorBrush(Color.Parse("#26262B")), new Rect(tx, 600, 100, 90));
                g.FillRectangle(new SolidColorBrush(Color.Parse("#E9E6EE")), new Rect(tx + 100, 600, 100, 90));
                StatusIcon(g, v, new Rect(tx + 8, 610, 24, 24), Color.Parse("#FFFFFF"));
                StatusIcon(g, v, new Rect(tx + 40, 610, 48, 48), Color.Parse("#FFFFFF"));
                StatusIcon(g, v, new Rect(tx + 108, 610, 24, 24), Color.Parse("#3B3A45"));
                StatusIcon(g, v, new Rect(tx + 140, 610, 48, 48), Color.Parse("#3B3A45"));
            }
        });
        return LunaArt.Png(pixels, width * 4, 0, 0, width, H);
    }

    /// <summary>
    /// <c>PARDEH_LOGO_CONCEPTS=&lt;folder&gt;</c> writes <c>concepts.png</c> (every design in a row: sizes on dark, light and blue),
    /// <c>hero_&lt;name&gt;.png</c> (512 px on dark and light) and <c>launcher_&lt;name&gt;.png</c> (adaptive-icon masks with that design's layers);
    /// <c>PARDEH_LOGO_VARIANTS=Orb,Ring</c> limits it, <c>PARDEH_LOGO_FINALISTS=&lt;file&gt;</c> also writes that one sheet for the listed designs.
    /// </summary>
    [Fact]
    public void Concept_sheets()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_LOGO_CONCEPTS") is not { Length: > 0 } folder)
            return;
        EnsurePlatform();
        Directory.CreateDirectory(folder);
        var variants = Environment.GetEnvironmentVariable("PARDEH_LOGO_VARIANTS") is { Length: > 0 } list
            ? list.Split(',').Select(s => Enum.Parse<LogoVariant>(s.Trim())).ToArray()
            : Enum.GetValues<LogoVariant>().Where(v => !PardehLogoArt.IsImage(v)).ToArray(); // the vector designs (the pictures: VariantsSheet)

        var bgs = new[] { "#0E1020", "#F3F6FB", "#0E3599" };
        const int RowH = 200;
        int[] panelW = [390, 270, 270];
        var width = panelW.Sum();
        var pixels = LunaArt.Render(width, RowH * variants.Length, g =>
        {
            for (var row = 0; row < variants.Length; row++)
            {
                var set = PardehLogoArt.For(variants[row]);
                for (var b = 0; b < 3; b++)
                {
                    var left = panelW.Take(b).Sum();
                    g.FillRectangle(new SolidColorBrush(Color.Parse(bgs[b])), new Rect(left, row * RowH, panelW[b], RowH));
                    var x = left + 8.0;
                    foreach (var s in b == 0 ? new[] { 180, 96, 48, 24 } : new[] { 120, 64, 32, 24 })
                    {
                        set.Full.Draw(g, new Rect(x, row * RowH + 8, s, s));
                        x += s + 8;
                    }
                }
            }
        });
        File.WriteAllBytes(Path.Combine(folder, "concepts.png"), LunaArt.Png(pixels, width * 4, 0, 0, width, RowH * variants.Length));

        foreach (var variant in variants)
        {
            var set = PardehLogoArt.For(variant);
            var hero = LunaArt.Render(1100, 560, g =>
            {
                g.FillRectangle(new SolidColorBrush(Color.Parse("#0E1020")), new Rect(0, 0, 560, 560));
                g.FillRectangle(new SolidColorBrush(Color.Parse("#F3F6FB")), new Rect(560, 0, 540, 560));
                set.Full.Draw(g, new Rect(24, 24, 512, 512));
                set.Full.Draw(g, new Rect(572, 24, 512, 512));
            });
            File.WriteAllBytes(Path.Combine(folder, $"hero_{variant}.png"), LunaArt.Png(hero, 1100 * 4, 0, 0, 1100, 560));

            // Launcher: masks and the themed icon, 96 and 192 px, then the 24 dp status icon at 1x / 3x on a shade.
            var layers = new Dictionary<string, string>
            {
                ["bg"] = LogoExport.Vector(set.LauncherBackground, 108, null, "x"),
                ["fg"] = LogoExport.Vector(set.Full, 108, (set.LauncherScale, 54 - 50 * set.LauncherScale, 53 - 48 * set.LauncherScale), "x", 108),
                ["mono"] = LogoExport.Vector(set.Monochrome, 108, (set.LauncherScale, 54 - 50 * set.LauncherScale, 53 - 48 * set.LauncherScale), "x", 108),
                ["stat"] = LogoExport.Vector(set.Silhouette, 24, (1, 0, 2), "x"),
            };
            var sizes = new[] { 72, 144 };
            var w = 20 + sizes.Sum(s => s + 20) * 3 + 200;
            const int H = 340;
            var launcher = LunaArt.Render(w, H, g =>
            {
                g.FillRectangle(LunaArt.Vertical((Color.Parse("#2B3A55"), 0), (Color.Parse("#6B4E71"), 1)), new Rect(0, 0, w, H));
                var x = 20.0;
                for (var mask = 0; mask < 3; mask++)
                {
                    foreach (var s in sizes)
                    {
                        var icon = new Rect(x, 20, s, s);
                        var canvas = s * 108 / 72.0;
                        var rect = new Rect(icon.Center.X - canvas / 2, icon.Center.Y - canvas / 2, canvas, canvas);
                        Geometry shape = mask switch { 0 => new EllipseGeometry(icon), 1 => Squircle(icon), _ => new RectangleGeometry(icon, s * 0.28, s * 0.28) };
                        using (g.PushGeometryClip(shape))
                        {
                            LogoExport.DrawVector(g, layers["bg"], rect);
                            LogoExport.DrawVector(g, layers["fg"], rect);
                        }

                        // themed
                        var icon2 = new Rect(x, 20 + 170, s / 2.0, s / 2.0);
                        var canvas2 = canvas / 2;
                        using (g.PushGeometryClip(new EllipseGeometry(icon2)))
                        {
                            g.FillRectangle(new SolidColorBrush(Color.Parse("#D9E2FF")), icon2);
                            LogoExport.DrawVector(g, layers["mono"], new Rect(icon2.Center.X - canvas2 / 2, icon2.Center.Y - canvas2 / 2, canvas2, canvas2), Color.Parse("#1B3F96"));
                        }

                        x += s + 20;
                    }
                }

                g.FillRectangle(new SolidColorBrush(Color.Parse("#26262B")), new Rect(w - 190, 10, 90, 160));
                g.FillRectangle(new SolidColorBrush(Color.Parse("#E9E6EE")), new Rect(w - 100, 10, 90, 160));
                LogoExport.DrawVector(g, layers["stat"], new Rect(w - 180, 20, 24, 24), Color.Parse("#FFFFFF"));
                LogoExport.DrawVector(g, layers["stat"], new Rect(w - 150, 20, 72, 72), Color.Parse("#FFFFFF"));
                LogoExport.DrawVector(g, layers["stat"], new Rect(w - 90, 20, 24, 24), Color.Parse("#3B3A45"));
                LogoExport.DrawVector(g, layers["stat"], new Rect(w - 80, 60, 72, 72), Color.Parse("#3B3A45"));
            });
            File.WriteAllBytes(Path.Combine(folder, $"launcher_{variant}.png"), LunaArt.Png(launcher, w * 4, 0, 0, w, H));
        }

        if (Environment.GetEnvironmentVariable("PARDEH_LOGO_FINALISTS") is { Length: > 0 } file)
        {
            const int Cell = 360, Pad = 12;
            var sheetW = variants.Length * Cell;
            const int SheetH = 640;
            var sheet = LunaArt.Render(sheetW, SheetH, g =>
            {
                for (var i = 0; i < variants.Length; i++)
                {
                    var set = PardehLogoArt.For(variants[i]);
                    var x0 = i * Cell;
                    g.FillRectangle(new SolidColorBrush(Color.Parse("#0E1020")), new Rect(x0, 0, Cell, 400));
                    g.FillRectangle(new SolidColorBrush(Color.Parse("#F3F6FB")), new Rect(x0, 400, Cell, 120));
                    g.FillRectangle(new SolidColorBrush(Color.Parse("#0E3599")), new Rect(x0, 520, Cell, 120));
                    set.Full.Draw(g, new Rect(x0 + Pad, Pad, Cell - 2 * Pad, Cell - 2 * Pad));
                    var x = x0 + 20.0;
                    foreach (var s in new[] { 96, 48, 24 })
                    {
                        set.Full.Draw(g, new Rect(x, 408, s, s));
                        set.Full.Draw(g, new Rect(x, 528, s, s));
                        x += s + 10;
                    }
                }
            });
            File.WriteAllBytes(file, LunaArt.Png(sheet, sheetW * 4, 0, 0, sheetW, SheetH));
        }
    }

    /// <summary>One launcher icon as the launcher draws it: the 108 dp layers (tile, foreground or the tinted themed layer) shown through a mask in the central 72 dp.</summary>
    private static void LauncherIcon(DrawingContext g, LogoVariant variant, Rect icon, int mask, bool themed)
    {
        // The adaptive icon is 108 dp, of which the central 72 dp are shown through the mask.
        var canvas = icon.Width * 108 / 72;
        var rect = new Rect(icon.Center.X - canvas / 2, icon.Center.Y - canvas / 2, canvas, canvas);
        var shape = mask switch
        {
            0 => new EllipseGeometry(icon),
            1 => Squircle(icon),
            _ => (Geometry)new RectangleGeometry(icon, icon.Width * 0.28, icon.Width * 0.28),
        };
        using (g.PushGeometryClip(shape))
        {
            using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
            {
                var source = new Rect(0, 0, LogoExport.LayerPx, LogoExport.LayerPx);
                if (themed)
                {
                    g.FillRectangle(new SolidColorBrush(Color.Parse("#D9E2FF")), icon);
                    // The themed icon is the alpha of the layer in the system's accent colour.
                    using (g.PushOpacityMask(new ImageBrush(LogoExport.ToBitmap(LogoExport.Monochrome(variant), LogoExport.LayerPx)) { Stretch = Stretch.Fill }, rect))
                        g.FillRectangle(new SolidColorBrush(Color.Parse("#1B3F96")), rect);
                }
                else
                {
                    var (top, bottom) = LogoExport.TileColors(variant);
                    g.FillRectangle(LunaArt.Vertical((Color.Parse(top), 0), (Color.Parse(bottom), 1)), rect);
                    g.DrawImage(LogoExport.ToBitmap(LogoExport.Foreground(variant), LogoExport.LayerPx), source, rect);
                }
            }
        }
    }

    private static Geometry Squircle(Rect r)
    {
        // Superellipse |x|^4 + |y|^4 = 1.
        var ctx = new StreamGeometry();
        using (var c = ctx.Open())
        {
            const int N = 96;
            for (var i = 0; i <= N; i++)
            {
                var a = 2 * Math.PI * i / N;
                var cos = Math.Cos(a);
                var sin = Math.Sin(a);
                var x = Math.Sign(cos) * Math.Sqrt(Math.Abs(cos));
                var y = Math.Sign(sin) * Math.Sqrt(Math.Abs(sin));
                var p = new Point(r.Center.X + x * r.Width / 2, r.Center.Y + y * r.Height / 2);
                if (i == 0)
                    c.BeginFigure(p, true);
                else
                    c.LineTo(p);
            }

            c.EndFigure(true);
        }

        return ctx;
    }
}
