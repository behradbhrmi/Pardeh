using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pardeh.Core.Input;

/// <summary>
/// The keymap profiles: which one is in use, the user's own ones (kept in a JSON file), and every change to them.
/// Within a profile a key belongs to one command. Not thread safe: use it from the UI thread.
/// </summary>
public sealed class KeymapManager
{
    private const int FileVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "Ctrl+O", not "Ctrl+O"
    };

    private readonly string? _path;
    private readonly List<KeymapProfile> _users = [];
    private string _activeId = BuiltInKeymaps.VlcId;
    private Dictionary<KeyChord, string>? _lookup;

    /// <param name="path">Where the user's profiles are kept; null keeps them in memory only.</param>
    public KeymapManager(string? path = null)
    {
        _path = path;
        Load();
    }

    /// <summary>Raised after any change: the profile in use, a profile's name or keys, profiles added or removed.</summary>
    public event Action? Changed;

    /// <summary>What went wrong reading or writing the file, for the editor to show; null when all is well.</summary>
    public string? Problem { get; private set; }

    /// <summary>The built-in profiles, then the user's in the order they were made.</summary>
    public IReadOnlyList<KeymapProfile> Profiles => [.. BuiltInKeymaps.All, .. _users];

    public KeymapProfile Active => Find(_activeId) ?? BuiltInKeymaps.All[0];

    public KeymapProfile? Find(string? id) => Profiles.FirstOrDefault(p => p.Id == id);

    public void SetActive(KeymapProfile profile)
    {
        Require(profile);
        if (_activeId == profile.Id)
            return;
        _activeId = profile.Id;
        Commit();
    }

    // ---- Reading bindings --------------------------------------------------------------------

    /// <summary>The command a key runs in the profile in use, or null.</summary>
    public string? CommandFor(KeyChord chord)
    {
        if (_lookup is null)
        {
            _lookup = [];
            foreach (var (command, chords) in Resolve(Active))
            {
                foreach (var bound in chords)
                    _lookup[bound] = command;
            }
        }

        return _lookup.GetValueOrDefault(chord);
    }

    /// <summary>Every command that has keys in <paramref name="profile"/>, with them.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<KeyChord>> Resolve(KeymapProfile profile)
    {
        var result = new Dictionary<string, IReadOnlyList<KeyChord>>();
        var claimed = new HashSet<KeyChord>();
        var inherited = profile.IsBuiltIn ? null : Find(profile.BaseId)?.Bindings;

        void Take(string command, List<string> keys)
        {
            var chords = new List<KeyChord>();
            foreach (var text in keys)
            {
                if (KeyChord.TryParse(text, out var chord) && claimed.Add(chord))
                    chords.Add(chord);
            }

            if (chords.Count > 0)
                result[command] = chords;
        }

        // The profile's own bindings win over inherited ones: a later version may give a new command a key the user already uses.
        foreach (var (command, keys) in InCatalogOrder(profile.Bindings))
            Take(command, keys);
        if (inherited is not null)
        {
            foreach (var (command, keys) in InCatalogOrder(inherited))
            {
                if (!profile.Bindings.ContainsKey(command))
                    Take(command, keys);
            }
        }

        return result;
    }

    public IReadOnlyList<KeyChord> ChordsFor(string command, KeymapProfile? profile = null) =>
        Resolve(profile ?? Active).GetValueOrDefault(command) ?? [];

    /// <summary>The command that has <paramref name="chord"/> in <paramref name="profile"/>, or null.</summary>
    public string? OwnerOf(KeymapProfile profile, KeyChord chord) =>
        Resolve(profile).FirstOrDefault(pair => pair.Value.Contains(chord)).Key;

    /// <summary>The user changed this command's keys from what the base profile gives it.</summary>
    public bool IsModified(KeymapProfile profile, string command) => !profile.IsBuiltIn && profile.Bindings.ContainsKey(command);

    public bool HasChanges(KeymapProfile profile) => !profile.IsBuiltIn && profile.Bindings.Count > 0;

    // ---- Profiles ----------------------------------------------------------------------------

    /// <summary>A new profile with the keys of <paramref name="baseProfile"/> (a built-in), or with none.</summary>
    public KeymapProfile Create(string name, KeymapProfile? baseProfile = null)
    {
        if (baseProfile is not null && !baseProfile.IsBuiltIn)
            return Duplicate(baseProfile, name);
        var profile = new KeymapProfile(NewId(), UniqueName(name), baseProfile?.Id, builtIn: false);
        _users.Add(profile);
        Commit();
        return profile;
    }

    public KeymapProfile Duplicate(KeymapProfile source, string? name = null)
    {
        Require(source);
        var bindings = source.IsBuiltIn ? [] : Copy(source.Bindings);
        var profile = new KeymapProfile(NewId(), UniqueName(name ?? source.Name + " copy"), source.IsBuiltIn ? source.Id : source.BaseId, builtIn: false, bindings);
        _users.Add(profile);
        Commit();
        return profile;
    }

    /// <summary>
    /// <paramref name="profile"/> itself when it can be changed; for a built-in, a new copy of it, which is put into use
    /// if the built-in was.
    /// </summary>
    public KeymapProfile Editable(KeymapProfile profile)
    {
        Require(profile);
        if (!profile.IsBuiltIn)
            return profile;
        var wasActive = Active.Id == profile.Id;
        var copy = Duplicate(profile, profile.Name + " (custom)");
        if (wasActive)
            SetActive(copy);
        return copy;
    }

    public void Rename(KeymapProfile profile, string name)
    {
        RequireUser(profile);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == profile.Name)
            return;
        profile.Name = UniqueName(name, except: profile);
        Commit();
    }

    /// <summary>Removes a user's profile. Deleting the one in use goes back to its base, or to VLC.</summary>
    public void Delete(KeymapProfile profile)
    {
        RequireUser(profile);
        _users.Remove(profile);
        if (_activeId == profile.Id)
            _activeId = profile.BaseId ?? BuiltInKeymaps.VlcId;
        Commit();
    }

    // ---- Keys --------------------------------------------------------------------------------

    /// <summary>Gives <paramref name="command"/> one more key, taking it from the command that had it. Returns that command's id, or null.</summary>
    public string? AddChord(KeymapProfile profile, string command, KeyChord chord)
    {
        RequireUser(profile);
        if (chord.IsEmpty)
            return null;
        var owner = OwnerOf(profile, chord);
        if (owner == command)
            return null;
        if (owner is not null)
            Store(profile, owner, ChordsFor(owner, profile).Where(c => c != chord));
        Store(profile, command, [.. ChordsFor(command, profile), chord]);
        Commit();
        return owner;
    }

    public void RemoveChord(KeymapProfile profile, string command, KeyChord chord)
    {
        RequireUser(profile);
        var chords = ChordsFor(command, profile);
        if (!chords.Contains(chord))
            return;
        Store(profile, command, chords.Where(c => c != chord));
        Commit();
    }

    /// <summary>Back to the base profile's keys for one command, taking them from whichever commands have them now.</summary>
    public void ResetCommand(KeymapProfile profile, string command)
    {
        RequireUser(profile);
        var defaults = (Find(profile.BaseId)?.Bindings.GetValueOrDefault(command) ?? [])
            .Select(KeyChord.Parse).ToList();
        foreach (var chord in defaults)
        {
            if (OwnerOf(profile, chord) is { } owner && owner != command)
                Store(profile, owner, ChordsFor(owner, profile).Where(c => c != chord));
        }

        profile.Bindings.Remove(command);
        Commit();
    }

    public void ResetAll(KeymapProfile profile)
    {
        RequireUser(profile);
        profile.Bindings.Clear();
        Commit();
    }

    private void Store(KeymapProfile profile, string command, IEnumerable<KeyChord> chords)
    {
        var keys = chords.Select(c => c.ToString()).ToList();
        var original = Find(profile.BaseId)?.Bindings.GetValueOrDefault(command) ?? [];
        if (keys.SequenceEqual(original))
            profile.Bindings.Remove(command); // the same as the base: not a change
        else
            profile.Bindings[command] = keys;
        _lookup = null;
    }

    // ---- Sharing a profile -------------------------------------------------------------------

    /// <summary>One profile as a file to keep or share.</summary>
    public string Export(KeymapProfile profile)
    {
        Require(profile);
        // A built-in is written out in full as a change to nothing, so the file means the same in every version.
        return JsonSerializer.Serialize(new ExportFile(FileVersion, profile.Name, profile.BaseId, Copy(profile.Bindings)), Json);
    }

    /// <summary>Adds the profile in an exported file. Throws <see cref="FormatException"/> when the text is not one.</summary>
    public KeymapProfile Import(string text)
    {
        ExportFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExportFile>(text, Json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("This is not a keymap file.", ex);
        }

        if (file is not { PardehKeymap: > 0, Bindings: not null })
            throw new FormatException("This is not a Pardeh keymap file.");
        if (file.PardehKeymap > FileVersion)
            throw new FormatException("This keymap file comes from a newer Pardeh.");
        var bad = file.Bindings.Values.SelectMany(keys => keys ?? []).FirstOrDefault(key => !KeyChord.TryParse(key, out _));
        if (bad is not null)
            throw new FormatException($"The file has a key Pardeh cannot read: \"{bad}\".");

        var baseId = BuiltInKeymaps.All.Any(p => p.Id == file.Base) ? file.Base : null;
        var profile = new KeymapProfile(NewId(), UniqueName(file.Name ?? "Imported keys"), baseId, builtIn: false, Clean(file.Bindings));
        _users.Add(profile);
        Commit();
        return profile;
    }

    // ---- File --------------------------------------------------------------------------------

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
            return;
        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path), Json)
                       ?? throw new JsonException("empty");
            foreach (var saved in file.Profiles ?? [])
            {
                if (saved is not { Id.Length: > 0 } || Find(saved.Id) is not null)
                    continue;
                var baseId = BuiltInKeymaps.All.Any(p => p.Id == saved.Base) ? saved.Base : null;
                _users.Add(new KeymapProfile(saved.Id, string.IsNullOrWhiteSpace(saved.Name) ? "Keymap" : saved.Name, baseId, builtIn: false, Clean(saved.Bindings)));
            }

            _activeId = Find(file.Active)?.Id ?? BuiltInKeymaps.VlcId;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Problem = "The saved keymaps could not be read; the VLC keys are in use.";
            try
            {
                File.Copy(_path, _path + ".bad", overwrite: true); // the next save would replace it
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void Commit()
    {
        _lookup = null;
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);
            var file = new StoreFile(FileVersion, _activeId, [.. _users.Select(p => new SavedProfile(p.Id, p.Name, p.BaseId, p.Bindings))]);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, Json));
            File.Move(temporary, _path, overwrite: true);
            Problem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem = "The keymaps could not be saved: " + ex.Message;
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static IEnumerable<KeyValuePair<string, List<string>>> InCatalogOrder(Dictionary<string, List<string>> bindings)
    {
        foreach (var command in KeyCommandCatalog.All)
        {
            if (bindings.TryGetValue(command.Id, out var keys))
                yield return new(command.Id, keys);
        }
    }

    private static Dictionary<string, List<string>> Copy(Dictionary<string, List<string>> bindings) =>
        bindings.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());

    /// <summary>Keys in their stored form, unreadable ones dropped. Unknown commands are kept for the version that knows them.</summary>
    private static Dictionary<string, List<string>> Clean(Dictionary<string, List<string>>? bindings) =>
        (bindings ?? []).ToDictionary(
            pair => pair.Key,
            pair => (pair.Value ?? []).Select(k => KeyChord.TryParse(k, out var c) ? c.ToString() : null).OfType<string>().Distinct().ToList());

    private static string NewId() => "user-" + Guid.NewGuid().ToString("N")[..12];

    private string UniqueName(string name, KeymapProfile? except = null)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Keymap" : name.Trim();
        bool Taken(string candidate) => Profiles.Any(p => p != except && p.Name.Equals(candidate, StringComparison.CurrentCultureIgnoreCase));
        if (!Taken(name))
            return name;
        var number = 2;
        while (Taken($"{name} {number}"))
            number++;
        return $"{name} {number}";
    }

    private void Require(KeymapProfile profile)
    {
        if (!Profiles.Contains(profile))
            throw new ArgumentException("This keymap is not one of the manager's.", nameof(profile));
    }

    private void RequireUser(KeymapProfile profile)
    {
        Require(profile);
        if (profile.IsBuiltIn)
            throw new InvalidOperationException("Built-in keymaps cannot be changed; duplicate it first.");
    }

    private sealed record StoreFile(int Version, string? Active, List<SavedProfile>? Profiles);

    private sealed record SavedProfile(string Id, string? Name, string? Base, Dictionary<string, List<string>>? Bindings);

    private sealed record ExportFile(int PardehKeymap, string? Name, string? Base, Dictionary<string, List<string>>? Bindings);
}
