using Pardeh.Skinning.Wmp;
using Pardeh.Skinning.Winamp;

namespace Pardeh.Skinning.Model;

/// <summary>
/// A skin loader plug-in (docs/04): recognises one skin file format and turns its files into the shared
/// <see cref="SkinPackage"/> the engine runs. The host never needs to know which format a skin came from.
/// </summary>
public interface ISkinLoader
{
    /// <summary>Human readable format name, e.g. "Windows Media Player skin".</summary>
    string Name { get; }

    /// <summary>File extensions (with the dot) this loader handles.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Reads the skin file. Throws <see cref="InvalidDataException"/> or <see cref="IOException"/> when it can't.</summary>
    SkinPackage Load(string path);
}

/// <summary>The registered skin loaders. Windows Media Player (.wmz / .wms) and Winamp classic (.wsz) are built in.</summary>
public static class SkinLoaders
{
    private static readonly List<ISkinLoader> Registered = [new WmpSkinLoader(), new WinampClassicLoader()];

    public static IReadOnlyList<ISkinLoader> All => Registered;

    /// <summary>Adds a loader (e.g. from another assembly). The first loader that knows an extension wins.</summary>
    public static void Register(ISkinLoader loader)
    {
        lock (Registered)
            Registered.Add(loader);
    }

    /// <summary>Every extension a skin file can have, lower case with the dot.</summary>
    public static IEnumerable<string> Extensions => Registered.SelectMany(l => l.Extensions).Select(e => e.ToLowerInvariant()).Distinct();

    /// <summary>File picker patterns such as "*.wmz".</summary>
    public static IReadOnlyList<string> PickerPatterns => Extensions.Select(e => "*" + e).ToList();

    public static ISkinLoader? Find(string path)
    {
        var extension = Path.GetExtension(path);
        return Registered.FirstOrDefault(l => l.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public static bool IsSkinFile(string path) => Find(path) is not null;

    /// <summary>Loads a skin file with the loader for its extension.</summary>
    public static SkinPackage Load(string path) =>
        (Find(path) ?? throw new InvalidDataException($"Unknown skin format: {Path.GetExtension(path)}")).Load(path);
}

/// <summary>Windows Media Player 7 - 11 skins: .wmz archives and loose .wms definitions.</summary>
public sealed class WmpSkinLoader : ISkinLoader
{
    public string Name => "Windows Media Player skin";

    public IReadOnlyList<string> Extensions { get; } = [".wmz", ".wms"];

    public SkinPackage Load(string path) => WmzLoader.Load(path);
}
