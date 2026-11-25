using System.Globalization;
using System.Text.RegularExpressions;

namespace Pardeh.Skinning.Wmp;

/// <summary>
/// The classic player's own resources (scripts, bitmaps, strings), extracted from a user's copy of the player's
/// resource DLL into a folder: text/, bitmap/, image/, jpg/ and strings.tsv. Never shipped with Pardeh:
/// users import them from their own Windows installation media.
/// </summary>
public sealed partial class WmpResources
{
    private readonly string _folder;
    private readonly Lazy<Dictionary<int, string>> _strings;

    public WmpResources(string folder)
    {
        _folder = folder;
        _strings = new Lazy<Dictionary<int, string>>(LoadStrings);
    }

    public string Folder => _folder;

    /// <summary>Finds imported resources: PARDEH_WMP_RESOURCES, %APPDATA%\Pardeh\WMP9, or the repo's private/wmp9/res.</summary>
    public static WmpResources? Find()
    {
        foreach (var candidate in Candidates())
        {
            if (Directory.Exists(Path.Combine(candidate, "text")) && File.Exists(Path.Combine(candidate, "strings.tsv")))
                return new WmpResources(candidate);
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_WMP_RESOURCES") is { Length: > 0 } custom)
            yield return custom;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pardeh", "WMP9");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            yield return Path.Combine(dir.FullName, "private", "wmp9", "res");
    }

    [GeneratedRegex(@"^res://wmploc(?:\.dll)?/(?:(?<type>[A-Za-z_]+|#\d+)/)?(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    /// <summary>Splits "res://wmploc[.dll]/[TYPE/]#id-or-name".</summary>
    public static bool TryParse(string reference, out string type, out string name)
    {
        var match = ReferencePattern().Match(reference.Trim());
        type = match.Success ? match.Groups["type"].Value.ToUpperInvariant() : string.Empty;
        name = match.Success ? match.Groups["name"].Value : string.Empty;
        return match.Success;
    }

    public string? GetString(int id) => _strings.Value.GetValueOrDefault(id);

    /// <summary>Resolves "res://wmploc/RT_STRING/#2070" (or any res:// string reference).</summary>
    public string? GetString(string reference) =>
        TryParse(reference, out _, out var name) && TryId(name, out var id) ? GetString(id) : null;

    /// <summary>A script or skin file: "res://wmploc/RT_TEXT/#132", "…/RT_TEXT/taskbar.js".</summary>
    public byte[]? GetText(string reference)
    {
        if (!TryParse(reference, out _, out var name))
            return null;
        var file = Path.Combine(_folder, "text", name.TrimStart('#'));
        if (File.Exists(file))
            return File.ReadAllBytes(file);
        // Resource names are stored upper-case.
        var upper = Path.Combine(_folder, "text", name.TrimStart('#').ToUpperInvariant());
        return File.Exists(upper) ? File.ReadAllBytes(upper) : null;
    }

    /// <summary>An image: "res://wmploc/#2033", "…/RT_BITMAP/#294", "…/RT_IMAGE/#2024", or a named bitmap.</summary>
    public byte[]? GetImage(string reference)
    {
        if (!TryParse(reference, out _, out var name))
            return null;
        var key = name.TrimStart('#');
        foreach (var file in new[]
                 {
                     Path.Combine(_folder, "bitmap", key + ".bmp"),
                     Path.Combine(_folder, "image", key + ".bin"),
                     Path.Combine(_folder, "jpg", key + ".jpg"),
                 })
        {
            if (File.Exists(file))
                return File.ReadAllBytes(file);
        }

        return null;
    }

    private static bool TryId(string name, out int id) =>
        int.TryParse(name.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);

    private Dictionary<int, string> LoadStrings()
    {
        var strings = new Dictionary<int, string>();
        var path = Path.Combine(_folder, "strings.tsv");
        if (!File.Exists(path))
            return strings;
        var last = -1;
        foreach (var line in File.ReadLines(path))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0 && int.TryParse(line.AsSpan(0, tab), out var id))
            {
                strings[id] = line[(tab + 1)..].Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
                last = id;
            }
            else if (last >= 0)
            {
                // A raw line break inside a multi-line string ("Now" / "Playing").
                strings[last] += "\n" + line;
            }
        }

        return strings;
    }
}
