using System.IO.Compression;
using Avalonia;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Winamp;
using Pardeh.UI;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// A tiny synthetic Winamp classic skin drawn in code at the real sheet sizes (no Winamp skin is ever committed):
/// every sprite rectangle has its own colour, so a test can tell exactly where the loader cut it from.
/// </summary>
internal static class SyntheticWsz
{
    public const int Main = 0x303030;
    public const int Marker = 0xAA0000;
    public const int TitleActive = 0x112233;

    /// <summary>Sheet, file the loader cuts it to, x, y, width, height (the classic sprite layout).</summary>
    public static readonly (string Sheet, string File, int X, int Y, int W, int H)[] Sprites =
    [
        ("titlebar.bmp", "menu.bmp", 0, 0, 9, 9), ("titlebar.bmp", "menu_d.bmp", 0, 9, 9, 9),
        ("titlebar.bmp", "min.bmp", 9, 0, 9, 9), ("titlebar.bmp", "min_d.bmp", 9, 9, 9, 9),
        ("titlebar.bmp", "shade.bmp", 0, 18, 9, 9),
        ("titlebar.bmp", "close.bmp", 18, 0, 9, 9), ("titlebar.bmp", "close_d.bmp", 18, 9, 9, 9),
        ("cbuttons.bmp", "prev.bmp", 0, 0, 23, 18), ("cbuttons.bmp", "prev_d.bmp", 0, 18, 23, 18),
        ("cbuttons.bmp", "play.bmp", 23, 0, 23, 18), ("cbuttons.bmp", "play_d.bmp", 23, 18, 23, 18),
        ("cbuttons.bmp", "pause.bmp", 46, 0, 23, 18), ("cbuttons.bmp", "pause_d.bmp", 46, 18, 23, 18),
        ("cbuttons.bmp", "stop.bmp", 69, 0, 23, 18), ("cbuttons.bmp", "stop_d.bmp", 69, 18, 23, 18),
        ("cbuttons.bmp", "next.bmp", 92, 0, 22, 18), ("cbuttons.bmp", "next_d.bmp", 92, 18, 22, 18),
        ("cbuttons.bmp", "eject.bmp", 114, 0, 22, 16), ("cbuttons.bmp", "eject_d.bmp", 114, 16, 22, 16),
        ("shufrep.bmp", "repeat.bmp", 0, 0, 28, 15), ("shufrep.bmp", "repeat_on.bmp", 0, 30, 28, 15),
        ("shufrep.bmp", "shuffle.bmp", 28, 0, 47, 15), ("shufrep.bmp", "shuffle_on.bmp", 28, 30, 47, 15),
        ("shufrep.bmp", "eq.bmp", 0, 61, 23, 12), ("shufrep.bmp", "pl.bmp", 23, 61, 23, 12),
        ("monoster.bmp", "stereo_on.bmp", 0, 0, 29, 12), ("monoster.bmp", "stereo_off.bmp", 0, 12, 29, 12),
        ("monoster.bmp", "mono_off.bmp", 29, 12, 27, 12),
        ("playpaus.bmp", "ind_play.bmp", 0, 0, 9, 9), ("playpaus.bmp", "ind_pause.bmp", 9, 0, 9, 9), ("playpaus.bmp", "ind_stop.bmp", 18, 0, 9, 9),
        ("volume.bmp", "volthumb.bmp", 15, 422, 14, 11), ("volume.bmp", "volthumb_d.bmp", 0, 422, 14, 11),
        ("balance.bmp", "balthumb.bmp", 15, 422, 14, 11), ("balance.bmp", "balthumb_d.bmp", 0, 422, 14, 11),
        ("posbar.bmp", "pos_thumb.bmp", 248, 0, 29, 10), ("posbar.bmp", "pos_thumb_d.bmp", 278, 0, 29, 10),
    ];

    private static readonly (string Name, int W, int H)[] Sheets =
    [
        ("titlebar.bmp", 344, 87), ("cbuttons.bmp", 136, 36), ("shufrep.bmp", 92, 85), ("monoster.bmp", 56, 24),
        ("playpaus.bmp", 42, 9), ("volume.bmp", 68, 433), ("balance.bmp", 47, 433), ("posbar.bmp", 307, 10),
    ];

    /// <summary>A unique, repeatable colour for sprite number <paramref name="index"/>.</summary>
    public static int ColorOf(int index) => ((index + 1) * 0x0B1C2D & 0xFFFFFF) | 0x080808;

    public static int ColorOf(string file) => ColorOf(Array.FindIndex(Sprites, s => s.File == file));

    public static int FrameColor(int frame) => (frame * 9 + 5) << 16 | 0x3232;

    public static int DigitColor(int digit) => (digit * 20 + 30) << 16 | 0x64C8;

    public static int GlyphColor(int column, int row) => (column * 8 + 10) << 16 | (row * 40 + 10) << 8 | 0x63;

    public sealed class Canvas(int width, int height, int fill = 0)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public int[] Rgb { get; } = Enumerable.Repeat(fill, width * height).ToArray();

        public void Fill(int x, int y, int w, int h, int rgb)
        {
            for (var yy = y; yy < y + h && yy < Height; yy++)
            {
                for (var xx = x; xx < x + w && xx < Width; xx++)
                    Rgb[yy * Width + xx] = rgb;
            }
        }

        public SkinBitmap ToBitmap()
        {
            var pixels = new byte[Width * Height * 4];
            for (var i = 0; i < Rgb.Length; i++)
            {
                pixels[i * 4] = (byte)Rgb[i];
                pixels[i * 4 + 1] = (byte)(Rgb[i] >> 8);
                pixels[i * 4 + 2] = (byte)(Rgb[i] >> 16);
                pixels[i * 4 + 3] = 255;
            }

            return new SkinBitmap(Width, Height, pixels);
        }

        public byte[] ToBmp() => BmpEncoder.Encode(ToBitmap());

        /// <summary>An 8-bit RLE8 file (needs at most 256 colours): what most real Winamp sheets look like.</summary>
        public byte[] ToRle8Bmp()
        {
            var palette = Rgb.Distinct().ToList();
            Assert.True(palette.Count <= 256);
            var data = new List<byte>();
            for (var y = Height - 1; y >= 0; y--) // bottom-up
            {
                var x = 0;
                while (x < Width)
                {
                    var color = Rgb[y * Width + x];
                    var run = 1;
                    while (x + run < Width && run < 255 && Rgb[y * Width + x + run] == color)
                        run++;
                    data.Add((byte)run);
                    data.Add((byte)palette.IndexOf(color));
                    x += run;
                }

                data.AddRange([0, 0]); // end of line
            }

            data.AddRange([0, 1]); // end of bitmap
            var file = new List<byte> { (byte)'B', (byte)'M' };
            void Int(int v) => file.AddRange(BitConverter.GetBytes(v));
            Int(14 + 40 + 1024 + data.Count);
            Int(0);
            Int(14 + 40 + 1024);
            Int(40);
            Int(Width);
            Int(Height);
            file.AddRange([1, 0, 8, 0]);
            Int(1); // BI_RLE8
            Int(data.Count);
            Int(2835);
            Int(2835);
            Int(palette.Count);
            Int(0);
            for (var i = 0; i < 256; i++)
            {
                var c = i < palette.Count ? palette[i] : 0;
                file.AddRange([(byte)c, (byte)(c >> 8), (byte)(c >> 16), 0]);
            }

            file.AddRange(data);
            return file.ToArray();
        }
    }

    /// <summary>The skin as a .wsz archive. <paramref name="folder"/> puts every file in a sub-folder; <paramref name="rleMain"/> compresses main.bmp.</summary>
    public static byte[] Create(bool rleMain = false, string folder = "", bool lowerCase = false, Action<Dictionary<string, byte[]>>? edit = null)
    {
        var files = new Dictionary<string, byte[]>();
        var main = new Canvas(WinampClassicLoader.WindowWidth, WinampClassicLoader.WindowHeight, Main);
        main.Fill(250, 108, 4, 4, Marker); // below every control
        files["main.bmp"] = rleMain ? main.ToRle8Bmp() : main.ToBmp();

        foreach (var (name, w, h) in Sheets)
        {
            var sheet = new Canvas(w, h, 0x050505);
            foreach (var (s, file, x, y, sw, sh) in Sprites.Where(s => s.Sheet == name))
                sheet.Fill(x, y, sw, sh, ColorOf(file));
            if (name == "titlebar.bmp")
            {
                sheet.Fill(27, 0, 275, 14, TitleActive);
                sheet.Fill(27, 15, 275, 14, 0x445566);
            }

            if (name == "volume.bmp")
            {
                for (var i = 0; i < 28; i++) sheet.Fill(0, i * 15, 68, 13, FrameColor(i));
            }

            if (name == "balance.bmp")
            {
                for (var i = 0; i < 28; i++) sheet.Fill(9, i * 15, 38, 13, FrameColor(i));
            }

            if (name == "posbar.bmp")
                sheet.Fill(0, 0, 248, 10, 0x777777);
            files[name] = sheet.ToBmp();
        }

        var digits = new Canvas(99, 13, 0x010101);
        for (var d = 0; d < 10; d++)
            digits.Fill(d * 9, 0, 9, 13, DigitColor(d));
        files["numbers.bmp"] = digits.ToBmp();

        var text = new Canvas(155, 18, 0x020202);
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 31; col++)
                text.Fill(col * 5, row * 6, 5, 6, GlyphColor(col, row));
        }

        files["text.bmp"] = text.ToBmp();
        files["viscolor.txt"] = System.Text.Encoding.ASCII.GetBytes("1,2,3 // black\r\n\r\n10,20,30,\r\n300,0,0 // clamps\r\n");
        files["pledit.txt"] = System.Text.Encoding.ASCII.GetBytes("[Text]\r\nNormal=#00FF00\r\nCurrent=FFFFFF\r\nNormalBG=#000000\r\nSelectedBG=#0000C6\r\nFont=Arial\r\n");
        edit?.Invoke(files);

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                var entryName = (folder.Length > 0 ? folder + "/" : "") + (lowerCase ? name : name.ToUpperInvariant());
                using var stream = zip.CreateEntry(entryName).Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    public static SkinPackage Load(bool rleMain = false, string folder = "", bool lowerCase = false) =>
        WinampClassicLoader.LoadArchive(new MemoryStream(Create(rleMain, folder, lowerCase)), "C:/skins/Synthetic.wsz");
}

public class WinampSkinTests
{
    private static SkinBitmap Decode(SkinPackage package, string file) =>
        new BmpDecoder().Decode(package.ReadFile(file) ?? throw new Xunit.Sdk.XunitException($"{file} is not in the package"))
        ?? throw new Xunit.Sdk.XunitException($"{file} does not decode");

    private static (Pardeh.Skinning.Runtime.SkinSession Session, MediaPlayer Player, FakeEngine Engine, FakeHost Host) Start(SkinPackage? package = null)
    {
        var started = TestSkins.Start(package ?? SyntheticWsz.Load());
        for (var i = 0; i < 3; i++)
            started.Session.Tick(TimeSpan.FromMilliseconds(30));
        return started;
    }

    private static SkinElement Find(Pardeh.Skinning.Runtime.SkinSession session, string tag) =>
        session.CurrentView!.Elements.Single(e => e.Tag == tag);

    private static void Press(SkinElement element)
    {
        element.OnPointerPressed(2, 2);
        element.OnPointerReleased(2, 2, inside: true);
    }

    [Fact]
    public void The_loader_registry_routes_wsz_wmz_and_wms_and_ignores_media()
    {
        Assert.IsType<WinampClassicLoader>(SkinLoaders.Find("C:/x/Skin.WSZ"));
        Assert.IsType<WmpSkinLoader>(SkinLoaders.Find("C:/x/Skin.wmz"));
        Assert.IsType<WmpSkinLoader>(SkinLoaders.Find("C:/x/Skin.wms"));
        Assert.Null(SkinLoaders.Find("C:/x/song.mp3"));
        Assert.Null(SkinLoaders.Find("C:/x/skin.wal")); // modern Winamp skins: not implemented
        Assert.Contains("*.wsz", SkinLoaders.PickerPatterns);
        // Drag and drop and the command line use the same check.
        Assert.True(StartupOptions.IsSkinFile("C:/x/Skin.wsz"));
        Assert.True(StartupOptions.IsSkinFile("C:/x/Skin.WMZ"));
        Assert.False(StartupOptions.IsSkinFile("C:/x/song.mp3"));
    }

    [Fact]
    public void A_synthetic_skin_loads_as_the_fixed_275_by_116_window()
    {
        var package = SyntheticWsz.Load();
        Assert.Equal("Synthetic", package.Title);
        var view = Assert.Single(package.Views);
        Assert.Equal("275", view.GetAttribute("width"));
        Assert.Equal("116", view.GetAttribute("height"));

        var (session, _, _, _) = Start(package);
        var root = session.CurrentView!.Root;
        Assert.Equal(275, root.Width);
        Assert.Equal(116, root.Height);

        (string Tag, int X, int Y)[] expected =
        [
            ("PREVBUTTON", 16, 88), ("PLAYBUTTON", 39, 88), ("PAUSEBUTTON", 62, 88), ("STOPBUTTON", 85, 88), ("NEXTBUTTON", 108, 88),
            ("SHUFFLEBUTTON", 164, 89), ("REPEATBUTTON", 211, 89), ("SEEKSLIDER", 16, 72), ("VOLUMESLIDER", 107, 57),
            ("BALANCESLIDER", 177, 57), ("ANALYZER", 24, 43),
        ];
        foreach (var (tag, x, y) in expected)
        {
            var element = Find(session, tag);
            Assert.Equal((x, y), ((int)element.Left, (int)element.Top));
        }

        var buttons = session.CurrentView.Elements.Where(e => e.Tag == "BUTTON").Select(e => ((int)e.Left, (int)e.Top)).ToList();
        Assert.Contains((136, 89), buttons); // eject
        Assert.Contains((264, 3), buttons);  // close
        Assert.Contains((244, 3), buttons);  // minimize
        Assert.Contains((6, 3), buttons);    // menu
    }

    [Fact]
    public void Every_sprite_is_cut_from_its_place_in_the_sheet()
    {
        var package = SyntheticWsz.Load();
        foreach (var (_, file, _, _, w, h) in SyntheticWsz.Sprites)
        {
            var bitmap = Decode(package, file);
            Assert.Equal((w, h), (bitmap.Width, bitmap.Height));
            Assert.Equal(SyntheticWsz.ColorOf(file), bitmap.GetRgb(0, 0));
            Assert.Equal(SyntheticWsz.ColorOf(file), bitmap.GetRgb(w - 1, h - 1));
        }
    }

    [Fact]
    public void The_background_is_main_bmp_under_the_active_title_bar()
    {
        var background = Decode(SyntheticWsz.Load(), "bg.bmp");
        Assert.Equal((275, 116), (background.Width, background.Height));
        Assert.Equal(SyntheticWsz.TitleActive, background.GetRgb(5, 5));    // titlebar.bmp (27,0) laid over y 0-13
        Assert.Equal(SyntheticWsz.TitleActive, background.GetRgb(274, 13));
        Assert.Equal(SyntheticWsz.Main, background.GetRgb(5, 14));          // main.bmp below it
        Assert.Equal(SyntheticWsz.Marker, background.GetRgb(251, 109));
    }

    [Fact]
    public void Volume_and_balance_backgrounds_are_28_frames_and_follow_the_value()
    {
        var package = SyntheticWsz.Load();
        for (var i = 0; i < 28; i++)
        {
            Assert.Equal(SyntheticWsz.FrameColor(i), Decode(package, $"vol{i}.bmp").GetRgb(10, 5));
            var balance = Decode(package, $"bal{i}.bmp");
            Assert.Equal((38, 13), (balance.Width, balance.Height)); // 47 px sheet: the frames start 9 px in
            Assert.Equal(SyntheticWsz.FrameColor(i), balance.GetRgb(10, 5));
        }

        var (session, player, _, _) = Start(package);
        var volumeBackground = session.CurrentView!.Elements.Single(e => e.Tag == "SUBVIEW" && (int)e.Left == 107 && (int)e.Top == 57);
        var balanceBackground = session.CurrentView.Elements.Single(e => e.Tag == "SUBVIEW" && (int)e.Left == 177 && (int)e.Top == 57);
        player.Volume = 100;
        player.Balance = -100;
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("vol27.bmp", volumeBackground.GetString("backgroundImage"));
        Assert.Equal("bal27.bmp", balanceBackground.GetString("backgroundImage"));
        player.Volume = 0;
        player.Balance = 0;
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("vol0.bmp", volumeBackground.GetString("backgroundImage"));
        Assert.Equal("bal0.bmp", balanceBackground.GetString("backgroundImage"));
    }

    [Fact]
    public void Clock_title_indicator_and_seek_thumb_follow_the_player()
    {
        var (session, player, engine, _) = Start();
        var glyphs = session.CurrentView!.Elements.OfType<GlyphTextElement>().ToList();
        var minutes = glyphs.Single(g => (int)g.Left == 48);
        var seconds = glyphs.Single(g => (int)g.Left == 78);
        var title = glyphs.Single(g => (int)g.Left == 111);
        var indicator = session.CurrentView.Elements.Single(e => e.Tag == "SUBVIEW" && (int)e.Left == 24 && (int)e.Top == 28);
        var seek = Find(session, "SEEKSLIDER");

        Assert.Equal("PARDEH", title.Text);
        Assert.Equal("00", minutes.Text);
        Assert.Equal("ind_stop.bmp", indicator.GetString("backgroundImage"));
        Assert.Equal("pos_none.bmp", seek.GetString("thumbImage")); // no thumb while stopped

        player.OpenFiles(["C:/music/song.mp3"]);
        engine.Position = TimeSpan.FromSeconds(75);
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("01", minutes.Text);
        Assert.Equal("15", seconds.Text);
        Assert.StartsWith(player.CurrentMedia!.Name, title.Text);
        Assert.EndsWith(")", title.Text);
        Assert.Equal("ind_play.bmp", indicator.GetString("backgroundImage"));
        Assert.Equal("pos_thumb.bmp", seek.GetString("thumbImage"));

        player.Pause();
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("ind_pause.bmp", indicator.GetString("backgroundImage"));
    }

    [Fact]
    public void Buttons_drive_the_player_and_eject_opens_the_file_dialog()
    {
        var (session, player, engine, host) = Start();

        // Winamp's Play with an empty playlist asks for a file.
        Press(Find(session, "PLAYBUTTON"));
        Assert.Contains("openFile", host.Commands);

        host.Commands.Clear();
        var eject = session.CurrentView!.Elements.Single(e => e.Tag == "BUTTON" && (int)e.Left == 136);
        Press(eject);
        Assert.Contains("openFile", host.Commands);

        player.OpenFiles(["C:/music/a.mp3", "C:/music/b.mp3"]);
        session.Tick(TimeSpan.FromMilliseconds(30));
        var first = player.CurrentMedia;
        Press(Find(session, "NEXTBUTTON"));
        Assert.NotSame(first, player.CurrentMedia);
        Press(Find(session, "STOPBUTTON"));
        Assert.False(engine.Playing);

        Press(Find(session, "SHUFFLEBUTTON"));
        Assert.True(player.Shuffle);
        Press(Find(session, "REPEATBUTTON"));
        Assert.True(player.Loop);
    }

    [Fact]
    public void Dragging_the_volume_slider_sets_the_volume()
    {
        var (session, player, _, _) = Start();
        var volume = Find(session, "VOLUMESLIDER");
        volume.OnPointerPressed(61, 6); // far right: thumb 14 px wide, travel 54
        volume.OnPointerReleased(61, 6, inside: true);
        Assert.True(player.Volume > 95);
    }

    [Fact]
    public void Files_in_a_sub_folder_and_in_any_letter_case_are_found()
    {
        var upper = SyntheticWsz.Load(folder: "Skin");
        var lower = SyntheticWsz.Load(lowerCase: true, folder: "a/b");
        Assert.Equal(SyntheticWsz.Marker, Decode(upper, "bg.bmp").GetRgb(251, 109));
        Assert.Equal(SyntheticWsz.Marker, Decode(lower, "bg.bmp").GetRgb(251, 109));
    }

    [Fact]
    public void An_RLE8_compressed_main_bmp_loads_like_an_uncompressed_one()
    {
        var plain = Decode(SyntheticWsz.Load(), "bg.bmp");
        var rle = Decode(SyntheticWsz.Load(rleMain: true), "bg.bmp");
        Assert.Equal(plain.Pixels, rle.Pixels);
    }

    [Fact]
    public void Only_the_known_files_are_read_and_the_package_has_no_executables()
    {
        var bytes = SyntheticWsz.Create(edit: files =>
        {
            files["evil.dll"] = [0x4D, 0x5A];
            files["gen.bmp"] = new SyntheticWsz.Canvas(10, 10).ToBmp();
            files["readme.txt"] = "hello"u8.ToArray();
        });
        var package = WinampClassicLoader.LoadArchive(new MemoryStream(bytes), "x.wsz");
        Assert.DoesNotContain(package.FileNames, n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || n.StartsWith("gen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Broken_archives_are_refused_with_a_clear_error()
    {
        Assert.Contains("not a ZIP", Assert.Throws<InvalidDataException>(() =>
            WinampClassicLoader.LoadArchive(new MemoryStream("this is not a zip file at all"u8.ToArray()), "x.wsz")).Message);

        var noMain = SyntheticWsz.Create(edit: files => files.Remove("main.bmp"));
        Assert.Contains("main.bmp", Assert.Throws<InvalidDataException>(() =>
            WinampClassicLoader.LoadArchive(new MemoryStream(noMain), "x.wsz")).Message);

        var garbageMain = SyntheticWsz.Create(edit: files => files["main.bmp"] = [1, 2, 3, 4, 5]);
        Assert.Throws<InvalidDataException>(() => WinampClassicLoader.LoadArchive(new MemoryStream(garbageMain), "x.wsz"));

        // A 9 MB "main.bmp" (a few KB zipped): over the per-file limit.
        var huge = SyntheticWsz.Create(edit: files => files["main.bmp"] = new byte[9 * 1024 * 1024]);
        Assert.Contains("too large", Assert.Throws<InvalidDataException>(() =>
            WinampClassicLoader.LoadArchive(new MemoryStream(huge), "x.wsz")).Message);
    }

    [Fact]
    public void A_skin_with_missing_sheets_still_loads_without_those_controls()
    {
        var bytes = SyntheticWsz.Create(edit: files =>
        {
            files.Remove("cbuttons.bmp");
            files.Remove("text.bmp");
            files.Remove("posbar.bmp");
        });
        var package = WinampClassicLoader.LoadArchive(new MemoryStream(bytes), "x.wsz");
        var (session, _, _, _) = Start(package);
        Assert.DoesNotContain(session.CurrentView!.Elements, e => e.Tag is "PLAYBUTTON" or "SEEKSLIDER");
        Assert.DoesNotContain(session.CurrentView.Elements.OfType<GlyphTextElement>(), g => (int)g.Left == 111);
        Assert.Contains(session.CurrentView.Elements, e => e.Tag == "VOLUMESLIDER");
    }

    /// <summary>Real skins the developer keeps in the git-ignored private/skins folder (none is ever committed).</summary>
    [Fact]
    public void Real_wsz_skins_in_the_private_folder_load_and_start()
    {
        var folder = TestSkins.FindPrivate("skins");
        var skins = folder is null ? [] : Directory.EnumerateFiles(folder, "*.wsz").ToList();
        Assert.SkipWhen(skins.Count == 0, "no .wsz skins in private/skins (they are never committed)");
        foreach (var path in skins)
        {
            var package = SkinLoaders.Load(path);
            var (session, _, _, _) = Start(package);
            Assert.Equal(275, session.CurrentView!.Root.Width);
            Assert.Contains(session.CurrentView.Elements, e => e.Tag == "PLAYBUTTON");
        }
    }

    [Fact]
    public void Viscolor_and_playlist_colours_are_parsed()
    {
        var colors = WinampClassicLoader.ParseVisColors("1,2,3 // black\r\n\r\n10,20,30,\r\n300,0,0 // clamps\r\n");
        Assert.Equal(24, colors.Length);
        Assert.Equal(0x010203, colors[0]);
        Assert.Equal(0x0A141E, colors[1]);
        Assert.Equal(0xFF0000, colors[2]);
        Assert.Equal(AnalyzerElement.DefaultColors[3], colors[3]); // not given: Winamp's default
        Assert.Equal(AnalyzerElement.DefaultColors, WinampClassicLoader.ParseVisColors(null));

        var playlist = WinampClassicLoader.ParsePlaylistColors("[Text]\r\nNormal=#00FF00\r\nCurrent=FFFFFF\r\nFont=Arial\r\n[Other]\r\nNormalBG=#123456\r\n");
        Assert.Contains(("Normal", "#00FF00"), playlist);
        Assert.Contains(("Current", "#FFFFFF"), playlist);
        Assert.Contains(("Font", "Arial"), playlist);
        Assert.DoesNotContain(playlist, p => p.Key == "NormalBG");

        var theme = SyntheticWsz.Load().Theme;
        Assert.Equal("#00FF00", theme.GetAttribute("winampPlaylistNormal"));
        Assert.StartsWith("#010203;#0A141E;#FF0000", theme.GetAttribute("winampVisColors"));
    }

    [Fact]
    public void Glyph_text_places_each_character_in_its_font_cell()
    {
        Assert.Equal((0, 0), GlyphTextElement.FindCell("text", 'a')); // lower case maps to the capitals
        Assert.Equal((25, 0), GlyphTextElement.FindCell("text", 'Z'));
        Assert.Equal((0, 1), GlyphTextElement.FindCell("text", '0'));
        Assert.Equal((12, 1), GlyphTextElement.FindCell("text", ':'));
        Assert.Equal((3, 2), GlyphTextElement.FindCell("text", '?'));
        Assert.Null(GlyphTextElement.FindCell("text", ' '));
        Assert.Null(GlyphTextElement.FindCell("text", '\u4E2D')); // no such glyph: skipped
        Assert.Equal((7, 0), GlyphTextElement.FindCell("digits", '7'));
        Assert.Null(GlyphTextElement.FindCell("digits", 'x'));
    }

    [Fact]
    public void A_long_title_scrolls_and_a_short_one_stays_put()
    {
        var (session, _, _, _) = Start();
        var title = session.CurrentView!.Elements.OfType<GlyphTextElement>().Single(g => (int)g.Left == 111);
        title.Set("value", "SHORT");
        Assert.All(title.Layout(), g => Assert.InRange(g.X, 0.0, 154.0));
        Assert.Equal(0, title.Layout().First().X);

        title.Set("value", new string('A', 60)); // 60 cells of 5 px: wider than the 154 px window
        var xs = title.Layout().Select(g => g.X).ToList();
        Assert.True(xs.First() <= 0);  // starts at the left (a pause before it moves)
        Assert.True(xs.Count > 60);    // the repeat follows the title so the loop has no jump
    }

    // ---- real rendering through the Avalonia renderer (headless Skia) ----


    private static void EnsurePlatform() => TestPlatform.Ensure();

    private static int PixelAt(byte[] bgra, int x, int y) =>
        (bgra[(y * 275 + x) * 4 + 2] << 16) | (bgra[(y * 275 + x) * 4 + 1] << 8) | bgra[(y * 275 + x) * 4];

    [Collection("Avalonia rendering")]
    public sealed class Rendering
    {
        [Fact]
        public void The_skin_renders_background_buttons_clock_and_title_from_the_sheets()
        {
            EnsurePlatform();
            var (session, player, engine, _) = Start();
            player.OpenFiles(["C:/music/song.mp3"]);
            engine.Position = TimeSpan.FromSeconds(75); // 01:15
            player.Volume = 100;
            session.Tick(TimeSpan.FromMilliseconds(30));
            var view = session.CurrentView!;

            var pixels = LunaArt.Render(275, 116, g => new SkinRenderer().Render(g, view));

            Assert.Equal(SyntheticWsz.Main, PixelAt(pixels, 5, 60));                       // main.bmp
            Assert.Equal(SyntheticWsz.TitleActive, PixelAt(pixels, 5, 5));                  // title bar
            Assert.Equal(SyntheticWsz.Marker, PixelAt(pixels, 251, 109));
            Assert.Equal(SyntheticWsz.ColorOf("prev.bmp"), PixelAt(pixels, 16 + 2, 88 + 2));
            Assert.Equal(SyntheticWsz.ColorOf("play.bmp"), PixelAt(pixels, 39 + 2, 88 + 2));
            Assert.Equal(SyntheticWsz.ColorOf("stop.bmp"), PixelAt(pixels, 85 + 2, 88 + 2));
            Assert.Equal(SyntheticWsz.ColorOf("shuffle.bmp"), PixelAt(pixels, 164 + 5, 89 + 5));
            Assert.Equal(SyntheticWsz.ColorOf("close.bmp"), PixelAt(pixels, 264 + 3, 3 + 3));
            Assert.Equal(SyntheticWsz.ColorOf("stereo_on.bmp"), PixelAt(pixels, 239 + 3, 41 + 3)); // playing: stereo lit
            Assert.Equal(SyntheticWsz.ColorOf("ind_play.bmp"), PixelAt(pixels, 24 + 3, 28 + 3));
            Assert.Equal(SyntheticWsz.FrameColor(27), PixelAt(pixels, 107 + 4, 57 + 6)); // volume background frame for full volume

            // The clock: 01:15 at x 48 / 60 / 78 / 90, one font cell each.
            Assert.Equal(SyntheticWsz.DigitColor(0), PixelAt(pixels, 48 + 4, 26 + 6));
            Assert.Equal(SyntheticWsz.DigitColor(1), PixelAt(pixels, 60 + 4, 26 + 6));
            Assert.Equal(SyntheticWsz.DigitColor(1), PixelAt(pixels, 78 + 4, 26 + 6));
            Assert.Equal(SyntheticWsz.DigitColor(5), PixelAt(pixels, 90 + 4, 26 + 6));

            // The title marquee starts at the left: the first letter's cell of text.bmp.
            var title = view.Elements.OfType<GlyphTextElement>().Single(g => (int)g.Left == 111).Text;
            var (column, row) = GlyphTextElement.FindCell("text", title[0])!.Value;
            Assert.Equal(SyntheticWsz.GlyphColor(column, row), PixelAt(pixels, 111 + 2, 24 + 3));
        }

        [Fact]
        public void A_rle8_main_bmp_renders_the_same_window()
        {
            EnsurePlatform();
            var plain = Start(SyntheticWsz.Load()).Session.CurrentView!;
            var rle = Start(SyntheticWsz.Load(rleMain: true)).Session.CurrentView!;
            Assert.Equal(
                LunaArt.Render(275, 116, g => new SkinRenderer().Render(g, plain)),
                LunaArt.Render(275, 116, g => new SkinRenderer().Render(g, rle)));
        }
    }

    // ---- the BMP decoder's RLE paths ----

    [Fact]
    public void Rle8_runs_absolute_runs_delta_and_end_codes_decode()
    {
        // 4 x 3, 8-bit, palette: 0 black, 1 red, 2 green, 3 blue. Rows are stored bottom-up.
        byte[] data =
        [
            0x04, 0x01, 0x00, 0x00,                         // bottom row: four reds, end of line
            0x00, 0x03, 0x02, 0x03, 0x01, 0x00, 0x00, 0x00, // absolute run of 3 (green, blue, red) + pad byte, end of line
            0x00, 0x02, 0x03, 0x00, 0x02, 0x02, 0x00, 0x01, // delta: 3 right, 0 up; then two greens; end of bitmap
        ];
        var bmp = Rle8(4, 3, [0x000000, 0xFF0000, 0x00FF00, 0x0000FF], data);
        var bitmap = new BmpDecoder().Decode(bmp)!;
        Assert.Equal((4, 3), (bitmap.Width, bitmap.Height));
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(0xFF0000, bitmap.GetRgb(x, 2)));   // bottom file row = last image row
        Assert.Equal([0x00FF00, 0x0000FF, 0xFF0000, 0x000000], Enumerable.Range(0, 4).Select(x => bitmap.GetRgb(x, 1)).ToArray());
        Assert.Equal([0x000000, 0x000000, 0x000000, 0x00FF00], Enumerable.Range(0, 4).Select(x => bitmap.GetRgb(x, 0)).ToArray()); // the 4th pixel only: the second green is off the edge
    }

    [Fact]
    public void Truncated_rle_data_keeps_what_was_drawn_and_does_not_throw()
    {
        var bmp = Rle8(8, 2, [0x000000, 0xFF0000], [0x08, 0x01, 0x00, 0x00, 0x05]);
        var bitmap = new BmpDecoder().Decode(bmp)!;
        Assert.Equal(0xFF0000, bitmap.GetRgb(7, 1));
        Assert.Equal(0x000000, bitmap.GetRgb(0, 0));
    }

    private static byte[] Rle8(int width, int height, int[] palette, byte[] data)
    {
        var file = new List<byte> { (byte)'B', (byte)'M' };
        void Int(int v) => file.AddRange(BitConverter.GetBytes(v));
        Int(14 + 40 + 1024 + data.Length);
        Int(0);
        Int(14 + 40 + 1024);
        Int(40);
        Int(width);
        Int(height);
        file.AddRange([1, 0, 8, 0]);
        Int(1);
        Int(data.Length);
        Int(0);
        Int(0);
        Int(palette.Length);
        Int(0);
        for (var i = 0; i < 256; i++)
        {
            var c = i < palette.Length ? palette[i] : 0;
            file.AddRange([(byte)c, (byte)(c >> 8), (byte)(c >> 16), 0]);
        }

        file.AddRange(data);
        return file.ToArray();
    }
}
