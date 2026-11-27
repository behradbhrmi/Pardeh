using System.Text;
using System.Text.Json;
using Pardeh.Core.Changes;

namespace Pardeh.Core.Cleanup;

/// <summary>
/// What the user decided about cleanup: junk phrases and text to keep, songs to leave alone, and file name patterns.
/// One JSON file in the app data folder (null: memory only).
/// </summary>
public sealed class CleanupStore
{
    private sealed class Model
    {
        public List<string> Junk { get; set; } = [];
        public List<string> Keep { get; set; } = [];
        public List<string> Ignored { get; set; } = [];
        public List<string> Patterns { get; set; } = [];
        public string DefaultPattern { get; set; } = NamePattern.Presets[0];
        public bool RenameFiles { get; set; }
        public bool ShowBadges { get; set; } = true;
    }

    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly Model _model = new();
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);

    public CleanupStore(string? path = null)
    {
        _path = path;
        try
        {
            if (path is not null && File.Exists(path) && JsonSerializer.Deserialize<Model>(File.ReadAllText(path, Encoding.UTF8)) is { } loaded)
                _model = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file: the defaults.
        }

        _ignored.UnionWith(_model.Ignored);
    }

    /// <summary>Raised after a rule changed, so that songs are looked at again.</summary>
    public event EventHandler? RulesChanged;

    /// <summary>Raised after which songs are ignored, or whether badges show, changed.</summary>
    public event EventHandler? Changed;

    public JunkRules Rules
    {
        get
        {
            lock (_gate)
                return new JunkRules([.. _model.Junk], [.. _model.Keep]);
        }
    }

    public void SetRules(IEnumerable<string> junk, IEnumerable<string> keep)
    {
        static List<string> Tidy(IEnumerable<string> phrases) =>
            [.. phrases.Select(p => p.Trim()).Where(p => p.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase)];
        lock (_gate)
        {
            _model.Junk = Tidy(junk);
            _model.Keep = Tidy(keep);
            Save();
        }

        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsIgnored(string source)
    {
        lock (_gate)
            return _ignored.Contains(source);
    }

    public int IgnoredCount
    {
        get
        {
            lock (_gate)
                return _ignored.Count;
        }
    }

    public void SetIgnored(string source, bool ignored)
    {
        lock (_gate)
        {
            if (ignored ? !_ignored.Add(source) : !_ignored.Remove(source))
                return;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearIgnored()
    {
        lock (_gate)
        {
            _ignored.Clear();
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A file was renamed: an ignored song stays ignored.</summary>
    public void Move(string from, string to)
    {
        lock (_gate)
        {
            if (string.Equals(from, to, StringComparison.Ordinal) || !_ignored.Remove(from))
                return;
            _ignored.Add(to);
            Save();
        }
    }

    /// <summary>The user's own patterns; the presets are always offered too.</summary>
    public IReadOnlyList<string> OwnPatterns
    {
        get
        {
            lock (_gate)
                return [.. _model.Patterns];
        }
    }

    public IReadOnlyList<string> AllPatterns => [.. NamePattern.Presets, .. OwnPatterns.Where(p => !NamePattern.Presets.Contains(p))];

    public string DefaultPattern
    {
        get
        {
            lock (_gate)
                return NamePattern.Check(_model.DefaultPattern) is null ? _model.DefaultPattern : NamePattern.Presets[0];
        }
    }

    /// <summary>Whether "File name" starts ticked in the review. Off unless the user turns it on.</summary>
    public bool RenameFiles
    {
        get
        {
            lock (_gate)
                return _model.RenameFiles;
        }
    }

    public bool ShowBadges
    {
        get
        {
            lock (_gate)
                return _model.ShowBadges;
        }
    }

    public void SetShowBadges(bool show)
    {
        lock (_gate)
        {
            _model.ShowBadges = show;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves a pattern (when it is the user's own), makes it the default and remembers whether to rename.</summary>
    public void SetNaming(string pattern, bool renameFiles)
    {
        if (NamePattern.Check(pattern) is not null)
            return;
        lock (_gate)
        {
            if (!NamePattern.Presets.Contains(pattern) && !_model.Patterns.Contains(pattern))
                _model.Patterns.Add(pattern);
            _model.DefaultPattern = pattern;
            _model.RenameFiles = renameFiles;
            Save();
        }
    }

    public void RemovePattern(string pattern)
    {
        lock (_gate)
        {
            if (!_model.Patterns.Remove(pattern))
                return;
            if (_model.DefaultPattern == pattern)
                _model.DefaultPattern = NamePattern.Presets[0];
            Save();
        }
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            _model.Ignored = [.. _ignored];
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            SafeFiles.WriteDurably(_path, JsonSerializer.SerializeToUtf8Bytes(_model, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
