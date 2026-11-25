using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Runtime.Panes;

/// <summary>Skin Chooser: the installed skins, a preview, and Apply.</summary>
public sealed class SkinChooserModel : ListPaneModel
{
    private List<string> _paths = [];
    private List<string[]> _rows = [];
    private readonly Dictionary<string, (SkinPackage? Package, SkinBitmap? Preview)> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public SkinChooserModel(SkinSession session) : base(session) => Reload();

    public override string Title => "Skins";

    public override IReadOnlyList<ListColumn> Columns { get; } = [new("Skin", 1)];

    public override IReadOnlyList<string[]> Rows => _rows;

    public override int ActiveRow =>
        Session.Host.RunCommand("currentSkin", []) is string current ? _paths.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) : -1;

    public override IReadOnlyList<string> Buttons { get; } = ["Apply Skin", "Add Skin...", "Skins Folder", "Refresh"];

    public override bool HasDetails => true;

    public override string Status => _rows.Count == 1 ? "1 skin" : $"{_rows.Count} skins";

    public override string Details(int row)
    {
        if (row < 0 || row >= _paths.Count)
            return "Select a skin to see a preview. Double-click or Apply Skin to switch to skin mode.";
        if (IsFullModeEntry(_paths[row]))
            return $"{_rows[row][0]}\nBuilt into Pardeh.\nDouble-click or Apply Skin to use it.";
        var package = Load(_paths[row]).Package;
        var author = package?.Theme.GetAttribute("author");
        var lines = new List<string> { package?.Title ?? _rows[row][0] };
        if (!string.IsNullOrWhiteSpace(author))
            lines.Add("by " + author);
        lines.Add(Path.GetFileName(_paths[row]));
        return string.Join("\n", lines);
    }

    // The host draws the whole skin offscreen; until that is ready (or without such a host), its largest picture.
    public override SkinBitmap? Preview(int row) =>
        row < 0 || row >= _paths.Count ? null
        : Session.Host.RunCommand("skinPreview", [_paths[row]]) as SkinBitmap ?? Load(_paths[row]).Preview;

    public override void Activate(int row)
    {
        if (row >= 0 && row < _paths.Count)
            Session.Host.RunCommand("applySkin", [_paths[row]]);
    }

    public override void Press(int button, int selectedRow)
    {
        switch (button)
        {
            case 0:
                Activate(selectedRow < 0 ? ActiveRow : selectedRow);
                break;
            case 1:
                Session.Host.RunCommand("chooseSkin", []);
                break;
            case 2:
                Session.Host.RunCommand("openSkinsFolder", []);
                break;
            default:
                Reload();
                break;
        }
    }

    private void Reload()
    {
        _paths = (Session.Host.RunCommand("listSkins", []) as IEnumerable<string> ?? []).ToList();
        _rows = _paths.Select(p => new[] { IsFullModeEntry(p) ? FullModeName(p) : Path.GetFileNameWithoutExtension(p) }).ToList();
        _loaded.Clear();
        OnChanged();
    }

    /// <summary>The skin and a preview: its main view's background, else its largest image.</summary>
    private (SkinPackage? Package, SkinBitmap? Preview) Load(string path)
    {
        if (_loaded.TryGetValue(path, out var loaded))
            return loaded;
        if (IsFullModeEntry(path))
            return _loaded[path] = (null, null); // the host renders these (skinPreview)
        SkinPackage? package = null;
        SkinBitmap? preview = null;
        try
        {
            package = SkinLoaders.Load(path);
            var view = package.Views.FirstOrDefault();
            // The largest picture is almost always the skin's main body.
            var name = package.FileNames.Where(IsImage).OrderByDescending(n => package.ReadFile(n)?.Length ?? 0).FirstOrDefault();
            var key = view?.GetAttribute("transparencyColor") ?? view?.GetAttribute("clippingColor") ?? "#FF00FF";
            if (name is not null && !name.Contains(':') && package.ReadFile(name) is { } data && Session.Decoder.Decode(data) is { } bitmap)
                preview = SkinValue.ParseColor(key) is { } rgb ? bitmap.WithColorKey(rgb) : bitmap;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Session.Log($"Skin preview failed for {path}: {ex.Message}");
        }

        return _loaded[path] = (package, preview);
    }

    /// <summary>The host lists Pardeh's built-in full modes as "fullmode:&lt;look&gt;" beside the .wmz skins.</summary>
    private static bool IsFullModeEntry(string path) => path.StartsWith("fullmode:", StringComparison.OrdinalIgnoreCase);

    private string FullModeName(string path) =>
        Session.Host.RunCommand("skinDisplayName", [path]) as string ?? path["fullmode:".Length..];

    private static bool IsImage(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".bmp" or ".png" or ".jpg" or ".jpeg" or ".gif";
}

/// <summary>Radio Tuner: internet stations from the community Radio Browser directory, played through FFmpeg.</summary>
public sealed class RadioTunerModel : ListPaneModel
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly string[] Genres = ["Top Stations", "Pop", "Rock", "Jazz", "Classical", "Electronic", "News"];

    private sealed record Station(string Name, string Url, string Tags, string Country, int Bitrate, string Codec, string Homepage);

    private List<Station> _stations = [];
    private List<string[]> _rows = [];
    private string _status = "Loading stations…";
    private string _genre = Genres[0];
    private CancellationTokenSource? _loading;

    public RadioTunerModel(SkinSession session) : base(session) => Load(Genres[0]);

    public override string Title => "Radio Tuner";

    public override IReadOnlyList<ListColumn> Columns { get; } =
        [new("Station", 2.2), new("Genre", 1.4), new("Speed", 0.55, AlignRight: true), new("Country", 0.55)];

    public override IReadOnlyList<string[]> Rows => _rows;

    public override string Status => _status;

    public override IReadOnlyList<string> Buttons => ["Play", .. Genres];

    public override int ActiveRow
    {
        get
        {
            var source = Session.Player.CurrentMedia?.Source;
            return source is null ? -1 : _stations.FindIndex(s => s.Url == source);
        }
    }

    public override bool HasDetails => true;

    public override string Details(int row)
    {
        if (row < 0 || row >= _stations.Count)
            return "Double-click a station to listen.\n\nStation list: radio-browser.info, a free community directory.";
        var s = _stations[row];
        return string.Join("\n", new[]
        {
            s.Name,
            s.Tags.Length > 0 ? "Genre: " + s.Tags : null,
            s.Country.Length > 0 ? "Country: " + s.Country : null,
            s.Bitrate > 0 ? $"{s.Bitrate} Kbps {s.Codec}" : s.Codec,
            s.Homepage.Length > 0 ? s.Homepage : null,
        }.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    public override void Activate(int row)
    {
        if (row < 0 || row >= _stations.Count)
            return;
        var s = _stations[row];
        var item = new MediaItem(s.Url) { Name = s.Name };
        item.Attributes["Title"] = s.Name;
        item.Attributes["Genre"] = s.Tags;
        item.Attributes["Album"] = s.Name;
        Session.Player.OpenItems([item], 0);
        OnChanged();
    }

    public override void Press(int button, int selectedRow)
    {
        if (button == 0)
            Activate(selectedRow);
        else if (button - 1 < Genres.Length)
            Load(Genres[button - 1]);
    }

    public override void Close() => _loading?.Cancel();

    private async void Load(string genre)
    {
        _loading?.Cancel();
        var cancel = _loading = new CancellationTokenSource();
        _genre = genre;
        _status = "Loading stations…";
        OnChanged();

        var query = genre == Genres[0]
            ? "stations/topclick/60?hidebroken=true"
            : $"stations/bytag/{Uri.EscapeDataString(genre.ToLowerInvariant())}?order=clickcount&reverse=true&limit=60&hidebroken=true";
        try
        {
            using var stream = await Http.GetStreamAsync("https://all.api.radio-browser.info/json/" + query, cancel.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancel.Token);
            var stations = new List<Station>();
            foreach (var e in json.RootElement.EnumerateArray())
            {
                string Text(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;
                var url = Text("url_resolved") is { Length: > 0 } resolved ? resolved : Text("url");
                if (url.Length == 0 || Text("name").Length == 0)
                    continue;
                var tags = string.Join(", ", Text("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3));
                var bitrate = e.TryGetProperty("bitrate", out var b) && b.TryGetInt32(out var kbps) ? kbps : 0;
                stations.Add(new Station(Text("name"), url, tags, Text("countrycode"), bitrate, Text("codec"), Text("homepage")));
            }

            if (cancel.IsCancellationRequested)
                return;
            _stations = stations;
            _rows = stations.Select(s => new[]
            {
                s.Name, s.Tags, s.Bitrate > 0 ? s.Bitrate.ToString(CultureInfo.InvariantCulture) + "K" : string.Empty, s.Country,
            }).ToList();
            _status = $"{_genre}: {stations.Count} stations";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            _status = "Can't reach the station directory. Check your internet connection.";
            Session.Log("Radio Tuner: " + ex.Message);
        }

        OnChanged();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Pardeh/0.1");
        return client;
    }
}

/// <summary>
/// Settings (below the skins in the Settings pane): player, full mode, appearance, audio and library settings.
/// The host owns them: "settingsList" lists [section, name, value] rows, "changeSetting" (row) steps one to its
/// next value (or runs it, for actions), "options" opens Tools › Options.
/// </summary>
public sealed class SettingsModel : ListPaneModel
{
    private List<string[]> _rows = [];

    public SettingsModel(SkinSession session) : base(session) => Reload();

    public override string Title => "Settings";

    public override IReadOnlyList<ListColumn> Columns { get; } = [new("Section", 0.8), new("Setting", 2), new("Value", 1.1)];

    // Settings also change elsewhere (menus, Options, Ctrl+M): re-read them now and then while the pane is drawn.
    public override IReadOnlyList<string[]> Rows
    {
        get
        {
            if (DateTime.UtcNow - _read > TimeSpan.FromMilliseconds(500))
            {
                _read = DateTime.UtcNow;
                _rows = (Session.Host.RunCommand("settingsList", []) as IEnumerable<string[]> ?? []).ToList();
            }

            return _rows;
        }
    }

    private DateTime _read;

    public override string Status => "Double-click a setting (or Change) to switch it. Changes apply right away.";

    public override IReadOnlyList<string> Buttons { get; } = ["Change", "All Options..."];

    public override void Activate(int row)
    {
        if (row < 0)
            return;
        Session.Host.RunCommand("changeSetting", [row]);
        Reload();
    }

    public override void Press(int button, int selectedRow)
    {
        if (button == 0)
            Activate(selectedRow);
        else
            Session.Host.RunCommand("options", []);
    }

    private void Reload()
    {
        _read = default; // the change is applied asynchronously: read again on the next draw
        OnChanged();
    }
}

/// <summary>Copy to CD or Device: copies the Now Playing items to a removable drive or a folder.</summary>
public sealed class DeviceCopyModel : ListPaneModel
{
    private DateTime _drivesChecked;
    private List<(string Label, string Root)> _drives = [];

    public DeviceCopyModel(SkinSession session) : base(session)
    {
    }

    public override string Title => "Copy to CD or Device";

    public override IReadOnlyList<ListColumn> Columns { get; } =
        [new("Title", 2), new("Artist", 1.2), new("Length", 0.5, AlignRight: true), new("Size", 0.55, AlignRight: true)];

    private IReadOnlyList<MediaItem> Items => Session.Player.CurrentPlaylist.Items;

    public override IReadOnlyList<string[]> Rows => Items.Select(i => new[]
    {
        i.Name, i.GetAttribute("Author"), i.Duration > TimeSpan.Zero ? MediaPlayer.FormatTime(i.Duration.TotalSeconds) : string.Empty,
        SizeOf(i.Source) is { } bytes ? FormatSize(bytes) : string.Empty,
    }).ToList();

    public override string Status
    {
        get
        {
            var total = Items.Select(i => SizeOf(i.Source) ?? 0).Sum();
            var drives = Drives.Count == 0 ? "No removable device found; use Copy to Folder." : "Devices: " + string.Join(", ", Drives.Select(d => d.Label));
            return $"{Items.Count} items, {FormatSize(total)}   ·   {drives}";
        }
    }

    public override IReadOnlyList<string> Buttons => [.. Drives.Select(d => "Copy to " + d.Label), "Copy to Folder..."];

    public override void Press(int button, int selectedRow)
    {
        var files = Items.Select(i => i.Source).Where(File.Exists).Cast<object?>();
        var destination = button < Drives.Count ? Drives[button].Root : string.Empty;
        Session.Host.RunCommand("copyToDevice", [destination, .. files]);
    }

    private List<(string Label, string Root)> Drives
    {
        get
        {
            if (DateTime.UtcNow - _drivesChecked < TimeSpan.FromSeconds(3))
                return _drives;
            _drivesChecked = DateTime.UtcNow;
            try
            {
                _drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .Select(d => ((d.VolumeLabel.Length > 0 ? d.VolumeLabel + " " : string.Empty) + "(" + d.Name.TrimEnd('\\') + ")", d.RootDirectory.FullName))
                    .ToList();
            }
            catch (IOException)
            {
                _drives = [];
            }

            return _drives;
        }
    }

    private static long? SizeOf(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 30 ? $"{bytes / (double)(1 << 30):0.0} GB" : $"{bytes / (double)(1 << 20):0.0} MB";
}
