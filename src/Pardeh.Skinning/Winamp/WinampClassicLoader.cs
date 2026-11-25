using System.Globalization;
using System.IO.Compression;
using System.Text;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Winamp;

/// <summary>
/// Winamp 2.x "classic" skins (.wsz): a ZIP of fixed-layout bitmaps. The loader cuts the sheets into the per-state
/// images of the skin engine and lays them out as the fixed 275 × 116 main window, as a WMS-style definition tree,
/// so the rest of Pardeh (renderer, hit testing, scripts, window shape) treats it like any other skin.
/// </summary>
/// <remarks>
/// Supported: main.bmp, titlebar.bmp, cbuttons.bmp, shufrep.bmp, monoster.bmp, playpaus.bmp, numbers.bmp (or nums_ex.bmp),
/// text.bmp, volume.bmp, balance.bmp, posbar.bmp, viscolor.txt (the spectrum bars) and pledit.txt (read and kept as
/// theme attributes). Not supported: region.txt (window shapes: the window stays rectangular), the equalizer and playlist
/// windows (eqmain.bmp, pledit.bmp, gen.bmp …), window-shade mode, double size, kbps / kHz text, and RLE-compressed BMPs.
/// Archives are untrusted: only the known file names are read, into memory, with size limits.
/// </remarks>
public sealed class WinampClassicLoader : ISkinLoader
{
    public const int WindowWidth = 275;
    public const int WindowHeight = 116;

    private const int MaxEntries = 4096;
    private const long MaxEntrySize = 8L * 1024 * 1024;

    private static readonly HashSet<string> KnownFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "main.bmp", "titlebar.bmp", "cbuttons.bmp", "shufrep.bmp", "monoster.bmp", "playpaus.bmp", "numbers.bmp", "nums_ex.bmp",
        "text.bmp", "volume.bmp", "balance.bmp", "posbar.bmp", "viscolor.txt", "pledit.txt", "region.txt",
    };

    public string Name => "Winamp classic skin";

    public IReadOnlyList<string> Extensions { get; } = [".wsz"];

    public SkinPackage Load(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadArchive(stream, path);
    }

    public static SkinPackage LoadArchive(Stream stream, string sourcePath)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("This is not a Winamp skin (the file is not a ZIP archive).");
        }

        using (zip)
            return Build(ReadKnownFiles(zip), sourcePath);
    }

    /// <summary>Reads the skin's known files by base name (some skins keep them in a sub-folder; the shallowest wins).</summary>
    private static Dictionary<string, byte[]> ReadKnownFiles(ZipArchive zip)
    {
        if (zip.Entries.Count > MaxEntries)
            throw new InvalidDataException($"Skin has too many files ({zip.Entries.Count}).");

        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var full = entry.FullName.Replace('\\', '/');
            var name = full[(full.LastIndexOf('/') + 1)..];
            if (name.Length == 0 || !KnownFiles.Contains(name))
                continue;
            var depth = full.Count(c => c == '/');
            if (depths.TryGetValue(name, out var existing) && existing <= depth)
                continue;
            if (entry.Length > MaxEntrySize)
                throw new InvalidDataException($"Skin file '{entry.FullName}' is too large.");

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream((int)entry.Length);
            var chunk = new byte[16384];
            int read;
            while ((read = entryStream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaxEntrySize)
                    throw new InvalidDataException($"Skin file '{entry.FullName}' is larger than declared.");
                buffer.Write(chunk, 0, read);
            }

            depths[name] = depth;
            files[name] = buffer.ToArray();
        }

        return files;
    }

    private static SkinPackage Build(Dictionary<string, byte[]> source, string sourcePath)
    {
        var decoder = new BmpDecoder();
        var sheets = new Dictionary<string, SkinBitmap?>(StringComparer.OrdinalIgnoreCase);
        SkinBitmap? Sheet(string name)
        {
            if (!sheets.TryGetValue(name, out var bitmap))
                sheets[name] = bitmap = source.TryGetValue(name, out var data) ? decoder.Decode(data) : null;
            return bitmap;
        }

        var main = Sheet("main.bmp")
                   ?? throw new InvalidDataException("This is not a Winamp classic skin: main.bmp is missing or is not a readable bitmap.");

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        // Background: main.bmp with the active title bar laid over its top strip.
        var background = Compose(WindowWidth, WindowHeight, main);
        if (Sheet("titlebar.bmp") is { } titleSheet && Crop(titleSheet, 27, 0, WindowWidth, 14) is { } titleStrip)
            Blit(background, titleStrip, 0, 0);
        files["bg.bmp"] = BmpEncoder.Encode(background);

        // Cuts a sprite out of a sheet into its own file; null when the sheet or the rectangle is missing.
        string? Sprite(string file, string sheet, int x, int y, int w, int h)
        {
            if (Sheet(sheet) is not { } bitmap || Crop(bitmap, x, y, w, h) is not { } piece)
                return null;
            files[file] = BmpEncoder.Encode(piece);
            return file;
        }

        var viscolor = ParseVisColors(source.TryGetValue("viscolor.txt", out var visBytes) ? SkinPackage.DecodeText(visBytes) : null);
        var playlist = ParsePlaylistColors(source.TryGetValue("pledit.txt", out var pledit) ? SkinPackage.DecodeText(pledit) : null);

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var theme = new SkinNode("THEME");
        theme.SetAttribute("id", "winamp-" + stem);
        theme.SetAttribute("title", stem);
        theme.SetAttribute("winampClassic", "true");
        theme.SetAttribute("winampVisColors", string.Join(';', viscolor.Select(c => $"#{c:X6}")));
        foreach (var (key, value) in playlist)
            theme.SetAttribute("winampPlaylist" + key, value);

        var view = new SkinNode("VIEW");
        view.SetAttribute("id", "winampMain");
        view.SetAttribute("width", WindowWidth.ToString(CultureInfo.InvariantCulture));
        view.SetAttribute("height", WindowHeight.ToString(CultureInfo.InvariantCulture));
        view.SetAttribute("titleBar", "false");
        view.SetAttribute("resizable", "false");
        theme.AddChild(view);

        SkinNode Add(string tag, int x, int y, int w, int h, params (string Name, string? Value)[] attributes)
        {
            var node = new SkinNode(tag);
            node.SetAttribute("left", x.ToString(CultureInfo.InvariantCulture));
            node.SetAttribute("top", y.ToString(CultureInfo.InvariantCulture));
            node.SetAttribute("width", w.ToString(CultureInfo.InvariantCulture));
            node.SetAttribute("height", h.ToString(CultureInfo.InvariantCulture));
            foreach (var (name, value) in attributes)
            {
                if (value is not null)
                    node.SetAttribute(name, value);
            }

            view.AddChild(node);
            return node;
        }

        // A button needs its normal image; without the sheet the area simply shows main.bmp.
        void Button(string tag, int x, int y, int w, int h, string? image, string? down, params (string Name, string? Value)[] more)
        {
            if (image is null)
                return;
            Add(tag, x, y, w, h, [("image", image), ("downImage", down), .. more]);
        }

        // The background is a subview, not the view's own image: only elements catch the mouse that drags the window.
        Add("SUBVIEW", 0, 0, WindowWidth, WindowHeight, ("backgroundImage", "bg.bmp"));

        // ---- title bar buttons (titlebar.bmp) ----
        Button("BUTTON", 6, 3, 9, 9, Sprite("menu.bmp", "titlebar.bmp", 0, 0, 9, 9), Sprite("menu_d.bmp", "titlebar.bmp", 0, 9, 9, 9),
            ("onclick", "pardeh.command('menu', 'File');"), ("toolTip", "Menu"));
        Button("BUTTON", 244, 3, 9, 9, Sprite("min.bmp", "titlebar.bmp", 9, 0, 9, 9), Sprite("min_d.bmp", "titlebar.bmp", 9, 9, 9, 9),
            ("onclick", "view.minimize();"), ("toolTip", "Minimize"));
        if (Sprite("shade.bmp", "titlebar.bmp", 0, 18, 9, 9) is { } shade)
            Add("SUBVIEW", 254, 3, 9, 9, ("backgroundImage", shade)); // window-shade mode isn't supported: drawn, not clickable
        Button("BUTTON", 264, 3, 9, 9, Sprite("close.bmp", "titlebar.bmp", 18, 0, 9, 9), Sprite("close_d.bmp", "titlebar.bmp", 18, 9, 9, 9),
            ("onclick", "view.close();"), ("toolTip", "Close"));

        // ---- transport (cbuttons.bmp) ----
        Button("PREVBUTTON", 16, 88, 23, 18, Sprite("prev.bmp", "cbuttons.bmp", 0, 0, 23, 18), Sprite("prev_d.bmp", "cbuttons.bmp", 0, 18, 23, 18),
            ("toolTip", "Previous"));
        Button("PLAYBUTTON", 39, 88, 23, 18, Sprite("play.bmp", "cbuttons.bmp", 23, 0, 23, 18), Sprite("play_d.bmp", "cbuttons.bmp", 23, 18, 23, 18),
            ("enabled", "true"), ("toolTip", "Play"),
            // With nothing in the playlist, Winamp's Play opens the file dialog.
            ("onclick", "if (player.currentPlaylist.count == 0) { pardeh.command('openFile'); } else { player.controls.play(); }"));
        Button("PAUSEBUTTON", 62, 88, 23, 18, Sprite("pause.bmp", "cbuttons.bmp", 46, 0, 23, 18), Sprite("pause_d.bmp", "cbuttons.bmp", 46, 18, 23, 18),
            ("toolTip", "Pause"));
        Button("STOPBUTTON", 85, 88, 23, 18, Sprite("stop.bmp", "cbuttons.bmp", 69, 0, 23, 18), Sprite("stop_d.bmp", "cbuttons.bmp", 69, 18, 23, 18),
            ("toolTip", "Stop"));
        Button("NEXTBUTTON", 108, 88, 22, 18, Sprite("next.bmp", "cbuttons.bmp", 92, 0, 22, 18), Sprite("next_d.bmp", "cbuttons.bmp", 92, 18, 22, 18),
            ("toolTip", "Next"));
        Button("BUTTON", 136, 89, 22, 16, Sprite("eject.bmp", "cbuttons.bmp", 114, 0, 22, 16), Sprite("eject_d.bmp", "cbuttons.bmp", 114, 16, 22, 16),
            ("onclick", "pardeh.command('openFile');"), ("toolTip", "Open file"));

        // ---- shuffle / repeat (shufrep.bmp): the "selected" image is the latched one ----
        Button("SHUFFLEBUTTON", 164, 89, 47, 15, Sprite("shuffle.bmp", "shufrep.bmp", 28, 0, 47, 15), Sprite("shuffle_on.bmp", "shufrep.bmp", 28, 30, 47, 15),
            ("toolTip", "Shuffle"));
        Button("REPEATBUTTON", 211, 89, 28, 15, Sprite("repeat.bmp", "shufrep.bmp", 0, 0, 28, 15), Sprite("repeat_on.bmp", "shufrep.bmp", 0, 30, 28, 15),
            ("toolTip", "Repeat"));

        // The equalizer and playlist toggles belong to windows Pardeh doesn't build: shown, not clickable.
        if (Sprite("eq.bmp", "shufrep.bmp", 0, 61, 23, 12) is { } eq)
            Add("SUBVIEW", 219, 58, 23, 12, ("backgroundImage", eq));
        if (Sprite("pl.bmp", "shufrep.bmp", 23, 61, 23, 12) is { } pl)
            Add("SUBVIEW", 242, 58, 23, 12, ("backgroundImage", pl));

        // ---- mono / stereo (monoster.bmp): no channel information yet, so stereo lights up while playing ----
        var stereoOn = Sprite("stereo_on.bmp", "monoster.bmp", 0, 0, 29, 12);
        var stereoOff = Sprite("stereo_off.bmp", "monoster.bmp", 0, 12, 29, 12);
        if (stereoOn is not null && stereoOff is not null)
            Add("SUBVIEW", 239, 41, 29, 12, ("backgroundImage", "wmpprop:(player.playState == psPlaying || player.playState == psPaused) ? 'stereo_on.bmp' : 'stereo_off.bmp'"));
        if (Sprite("mono_off.bmp", "monoster.bmp", 29, 12, 27, 12) is { } monoOff)
            Add("SUBVIEW", 212, 41, 27, 12, ("backgroundImage", monoOff));

        // ---- play / pause / stop indicator (playpaus.bmp) ----
        var playing = Sprite("ind_play.bmp", "playpaus.bmp", 0, 0, 9, 9);
        var paused = Sprite("ind_pause.bmp", "playpaus.bmp", 9, 0, 9, 9);
        var stopped = Sprite("ind_stop.bmp", "playpaus.bmp", 18, 0, 9, 9);
        if (playing is not null && paused is not null && stopped is not null)
            Add("SUBVIEW", 24, 28, 9, 9, ("backgroundImage", "wmpprop:player.playState == psPlaying ? 'ind_play.bmp' : (player.playState == psPaused ? 'ind_pause.bmp' : 'ind_stop.bmp')"));

        // ---- clock (numbers.bmp, else nums_ex.bmp) ----
        var digitSheet = Sheet("numbers.bmp") is not null ? "numbers.bmp" : Sheet("nums_ex.bmp") is not null ? "nums_ex.bmp" : null;
        if (digitSheet is not null)
        {
            files[digitSheet] = BmpEncoder.Encode(Sheet(digitSheet)!);
            const string Elapsed = "Math.floor(player.controls.currentPosition)";
            Add("GLYPHTEXT", 48, 26, 21, 13, ("glyphImage", digitSheet), ("glyphSet", "digits"), ("glyphWidth", "9"), ("glyphHeight", "13"), ("glyphSpacing", "3"),
                ("value", $"wmpprop:('0' + Math.floor({Elapsed} / 60) % 100).slice(-2)"));
            Add("GLYPHTEXT", 78, 26, 21, 13, ("glyphImage", digitSheet), ("glyphSet", "digits"), ("glyphWidth", "9"), ("glyphHeight", "13"), ("glyphSpacing", "3"),
                ("value", $"wmpprop:('0' + {Elapsed} % 60).slice(-2)"));
        }

        // ---- song title marquee (text.bmp) ----
        if (Sheet("text.bmp") is { } textSheet)
        {
            files["text.bmp"] = BmpEncoder.Encode(textSheet);
            Add("GLYPHTEXT", 111, 24, 154, 6, ("glyphImage", "text.bmp"), ("glyphSet", "text"), ("glyphWidth", "5"), ("glyphHeight", "6"), ("scroll", "true"),
                ("value", "wmpprop:player.currentMedia ? player.currentMedia.name + ' (' + player.currentMedia.durationString + ')' : 'PARDEH'"));
        }

        // ---- spectrum bars, coloured by viscolor.txt ----
        Add("ANALYZER", 24, 43, 76, 16, ("colors", theme.GetAttribute("winampVisColors")));

        // ---- volume (volume.bmp: 28 background frames 15 px apart, then the thumbs) ----
        var volumeThumb = Sprite("volthumb.bmp", "volume.bmp", 15, 422, 14, 11);
        var volumeThumbDown = Sprite("volthumb_d.bmp", "volume.bmp", 0, 422, 14, 11);
        var hasVolumeFrames = Frames("vol", "volume.bmp", 0, 68);
        if (hasVolumeFrames)
            Add("SUBVIEW", 107, 57, 68, 13, ("backgroundImage", "wmpprop:'vol' + Math.min(27, Math.round(player.settings.volume * 27 / 100)) + '.bmp'"));
        if (volumeThumb is not null)
            Add("VOLUMESLIDER", 107, 57, 68, 13, ("min", "0"), ("max", "100"), ("thumbImage", volumeThumb), ("thumbDownImage", volumeThumbDown), ("toolTip", "Volume"));

        // ---- balance (balance.bmp: frames start 9 px in when the sheet is 47 px wide) ----
        var balanceOffset = Sheet("balance.bmp") is { Width: >= 47 } ? 9 : 0;
        var balanceThumb = Sprite("balthumb.bmp", "balance.bmp", 15, 422, 14, 11);
        var balanceThumbDown = Sprite("balthumb_d.bmp", "balance.bmp", 0, 422, 14, 11);
        var hasBalanceFrames = Frames("bal", "balance.bmp", balanceOffset, 38);
        if (hasBalanceFrames)
            Add("SUBVIEW", 177, 57, 38, 13, ("backgroundImage", "wmpprop:'bal' + Math.min(27, Math.round(Math.abs(player.settings.balance) * 27 / 100)) + '.bmp'"));
        if (balanceThumb is not null)
            Add("BALANCESLIDER", 177, 57, 38, 13, ("min", "-100"), ("max", "100"), ("thumbImage", balanceThumb), ("thumbDownImage", balanceThumbDown), ("toolTip", "Balance"));

        // ---- seek bar (posbar.bmp: track 248 × 10, thumbs at x 248 and 278); the thumb only shows while a track plays ----
        var seekTrack = Sprite("pos_bg.bmp", "posbar.bmp", 0, 0, 248, 10);
        var seekThumb = Sprite("pos_thumb.bmp", "posbar.bmp", 248, 0, 29, 10);
        var seekThumbDown = Sprite("pos_thumb_d.bmp", "posbar.bmp", 278, 0, 29, 10);
        if (seekThumb is not null)
        {
            // A stand-in for "no thumb": the track's own pixels where an idle thumb rests (x 0, y 4), so it is invisible.
            if (Crop(Sheet("posbar.bmp")!, 0, 4, 1, 2) is { } idle)
                files["pos_none.bmp"] = BmpEncoder.Encode(idle);
            Add("SEEKSLIDER", 16, 72, 248, 10, ("backgroundImage", seekTrack), ("thumbImage",
                files.ContainsKey("pos_none.bmp") ? "wmpprop:(player.playState == psPlaying || player.playState == psPaused) ? 'pos_thumb.bmp' : 'pos_none.bmp'" : seekThumb),
                ("thumbDownImage", seekThumbDown), ("toolTip", "Seek"));
        }

        return Wmp.WmzLoader.FromNode(theme, sourcePath, files);

        // Cuts the 28 frames of volume.bmp / balance.bmp (13 px tall, one every 15 px) into files <prefix>0.bmp … <prefix>27.bmp.
        bool Frames(string prefix, string sheet, int x, int width)
        {
            var any = false;
            for (var i = 0; i < 28; i++)
                any |= Sprite($"{prefix}{i}.bmp", sheet, x, i * 15, width, 13) is not null;
            if (!any)
                return false;
            // A truncated sheet: the missing frames repeat the last one cut.
            for (var i = 1; i < 28; i++)
            {
                if (!files.ContainsKey($"{prefix}{i}.bmp"))
                    files[$"{prefix}{i}.bmp"] = files[$"{prefix}{i - 1}.bmp"];
            }

            return true;
        }
    }

    // ---- bitmap helpers ----

    /// <summary>A copy of the rectangle, or null when it doesn't lie fully inside the bitmap.</summary>
    private static SkinBitmap? Crop(SkinBitmap source, int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > source.Width || y + height > source.Height)
            return null;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(source.Pixels, ((y + row) * source.Width + x) * 4, pixels, row * width * 4, width * 4);
        return new SkinBitmap(width, height, pixels);
    }

    /// <summary>A canvas of the given size with <paramref name="source"/> at its top-left (cropped or black-padded).</summary>
    private static SkinBitmap Compose(int width, int height, SkinBitmap source)
    {
        var canvas = new SkinBitmap(width, height, new byte[width * height * 4]);
        for (var i = 3; i < canvas.Pixels.Length; i += 4)
            canvas.Pixels[i] = 255;
        Blit(canvas, source, 0, 0);
        return canvas;
    }

    private static void Blit(SkinBitmap target, SkinBitmap piece, int x, int y)
    {
        for (var row = 0; row < piece.Height && y + row < target.Height; row++)
        {
            var width = Math.Min(piece.Width, target.Width - x);
            if (width > 0)
                Buffer.BlockCopy(piece.Pixels, row * piece.Stride, target.Pixels, ((y + row) * target.Width + x) * 4, width * 4);
        }
    }

    // ---- text files ----

    /// <summary>viscolor.txt: 24 lines "r,g,b // comment". Missing or malformed lines keep Winamp's default colour.</summary>
    internal static int[] ParseVisColors(string? text)
    {
        var colors = (int[])Runtime.Elements.AnalyzerElement.DefaultColors.Clone();
        if (text is null)
            return colors;
        var index = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            if (index >= colors.Length)
                break;
            var line = rawLine;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                line = line[..comment];
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
                continue; // blank lines don't count
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                colors[index] = (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);
            index++;
        }

        return colors;
    }

    /// <summary>pledit.txt [Text] section: Normal, Current, NormalBG, SelectedBG, Font. Returned for a future playlist window.</summary>
    internal static List<(string Key, string Value)> ParsePlaylistColors(string? text)
    {
        var result = new List<(string, string)>();
        if (text is null)
            return result;
        var inText = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inText = line.StartsWith("[Text]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var equals = line.IndexOf('=');
            if (!inText || equals <= 0)
                continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                continue;
            // Colours are "#RRGGBB" (sometimes without the #).
            if (key.Equals("Font", StringComparison.OrdinalIgnoreCase))
                result.Add((key, value));
            else if (value.TrimStart('#').Length == 6 && int.TryParse(value.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                result.Add((key, $"#{rgb:X6}"));
        }

        return result;
    }
}
