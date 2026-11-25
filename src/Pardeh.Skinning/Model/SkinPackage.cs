using System.Text;

namespace Pardeh.Skinning.Model;

/// <summary>A loaded skin: its definition tree plus the files it references, all in memory.</summary>
public sealed class SkinPackage
{
    private readonly Dictionary<string, byte[]> _files;

    public SkinPackage(string sourcePath, SkinNode theme, Dictionary<string, byte[]> files)
    {
        SourcePath = sourcePath;
        Theme = theme;
        _files = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Path of the .wmz / .wms the skin came from.</summary>
    public string SourcePath { get; }

    /// <summary>The THEME node.</summary>
    public SkinNode Theme { get; }

    public string Id => Theme.Id ?? Path.GetFileNameWithoutExtension(SourcePath);

    public string Title => Theme.GetAttribute("title") ?? Id;

    public IEnumerable<SkinNode> Views => Theme.Children.Where(n => n.Tag == "VIEW");

    public IEnumerable<string> FileNames => _files.Keys;

    public SkinNode? FindView(string? id) =>
        id is null ? Views.FirstOrDefault() : Views.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public byte[]? ReadFile(string name)
    {
        name = NormalizeName(name);
        if (_files.TryGetValue(name, out var data))
            return data;

        // Skins sometimes reference files with a sub-folder that the archive doesn't have, or vice versa.
        var fileName = Path.GetFileName(name);
        foreach (var (key, value) in _files)
        {
            if (string.Equals(Path.GetFileName(key), fileName, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    public string? ReadText(string name)
    {
        var data = ReadFile(name);
        return data is null ? null : DecodeText(data);
    }

    public static string NormalizeName(string name) => name.Replace('\\', '/').TrimStart('/');

    /// <summary>Decodes skin text files: UTF-16 (with or without BOM), UTF-8, or ANSI.</summary>
    public static string DecodeText(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        if (data.Length >= 4 && data[1] == 0 && data[3] == 0)
            return Encoding.Unicode.GetString(data);

        // Valid UTF-8 or fall back to Latin-1 (close to Windows-1252 for skin text).
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }
}
