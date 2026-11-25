namespace Pardeh.Skinning.Runtime;

/// <summary>What the skin runtime needs from the windowing layer (implemented by the app).</summary>
public interface ISkinHost
{
    /// <summary>A new view became current (first view, or after a view switch).</summary>
    void ViewChanged(SkinView view);

    void ResizeView(SkinView view, double width, double height);

    /// <summary>The largest view size that fits on screen, in skin pixels.</summary>
    (double Width, double Height) MaxViewSize { get; }

    void MinimizeView(SkinView view);

    /// <summary>Starts an interactive resize from an edge ("bottomright", "left" …) during a mouse press.</summary>
    void BeginResize(SkinView view, string edge);

    /// <summary>The skin asked to close (view.close()).</summary>
    void CloseSkin();

    /// <summary>The skin's "return to full mode" button.</summary>
    void ReturnToFullMode();

    /// <summary>Synchronous file-open dialog. Returns the chosen path, or null.</summary>
    string? ShowOpenFileDialog(string dialogType, string filter);

    /// <summary>Asks the user before opening a URL from a skin.</summary>
    void LaunchUrl(string url);

    void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected);

    /// <summary>Shows a menu and waits for the choice (the classic POPUP.show()). Returns the index, or -1.</summary>
    int ShowPopupModal(SkinView view, IReadOnlyList<string> items);

    void ShowMessage(string text);

    /// <summary>Width in pixels of <paramref name="text"/> in the given font (for TEXT.textWidth).</summary>
    double MeasureText(string text, string fontFamily, double sizePoints, bool bold);

    /// <summary>An app command from Pardeh's own skins (pardeh.command("skinMode") …). Returns an optional result.</summary>
    object? RunCommand(string name, IReadOnlyList<object?> args);

    void Log(string message);
}

/// <summary>Per-skin key/value preferences (theme.savePreference / loadPreference).</summary>
public interface IPreferenceStore
{
    string? Load(string themeId, string key);

    void Save(string themeId, string key, string value);
}

/// <summary>Stores preferences as one JSON file per skin.</summary>
public sealed class JsonPreferenceStore(string directory) : IPreferenceStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? Load(string themeId, string key) => Read(themeId).GetValueOrDefault(key);

    public void Save(string themeId, string key, string value)
    {
        var values = Read(themeId);
        values[key] = value;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathFor(themeId), System.Text.Json.JsonSerializer.Serialize(values));
        }
        catch (IOException)
        {
            // Preferences are best effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private Dictionary<string, string> Read(string themeId)
    {
        if (_cache.TryGetValue(themeId, out var values))
            return values;

        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = PathFor(themeId);
            if (File.Exists(path))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded is not null)
                    values = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        _cache[themeId] = values;
        return values;
    }

    private string PathFor(string themeId)
    {
        var safe = string.Concat(themeId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(directory, safe + ".json");
    }
}
