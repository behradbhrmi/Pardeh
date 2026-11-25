namespace Pardeh.Core.Input;

/// <summary>
/// A named set of key bindings. A built-in one holds every binding itself. A user's profile holds only what differs from
/// the built-in it is <see cref="BaseId">based on</see> (an empty list: unbound), so a command added in a later version
/// arrives with its default key. A profile without a base starts with no keys.
/// </summary>
public sealed class KeymapProfile
{
    internal KeymapProfile(string id, string name, string? baseId, bool builtIn, Dictionary<string, List<string>>? bindings = null)
    {
        Id = id;
        Name = name;
        BaseId = baseId;
        IsBuiltIn = builtIn;
        Bindings = bindings ?? [];
    }

    public string Id { get; }

    public string Name { get; internal set; }

    /// <summary>The built-in profile this one changes, or null.</summary>
    public string? BaseId { get; }

    public bool IsBuiltIn { get; }

    /// <summary>Built-in: everything. A user's: the commands that differ from the base. Command id to key texts.</summary>
    internal Dictionary<string, List<string>> Bindings { get; }
}
