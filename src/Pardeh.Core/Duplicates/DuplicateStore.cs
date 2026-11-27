using System.Text.Json;
using Pardeh.Core.Changes;

namespace Pardeh.Core.Duplicates;

/// <summary>
/// What the user said about pairs of songs: "not duplicates" (never grouped again) and "not related" (not listed with each
/// other at all). A pair is remembered both by its sound, which renaming and retagging keep, and by where the files are.
/// One JSON file (null: memory only).
/// </summary>
public sealed class DuplicateStore
{
    private sealed record Model(int Version, List<string> Apart, List<string> Unrelated);

    private const char Between = '\n';

    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _apart = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unrelated = new(StringComparer.Ordinal);

    public DuplicateStore(string? path = null)
    {
        _path = path;
        try
        {
            if (path is not null && File.Exists(path) && JsonSerializer.Deserialize<Model>(File.ReadAllBytes(path)) is { Version: 1, Apart: { } apart, Unrelated: { } unrelated })
            {
                _apart.UnionWith(apart);
                _unrelated.UnionWith(unrelated);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }

    /// <summary>Raised after a change.</summary>
    public event EventHandler? Changed;

    private static string Pair(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0 ? first + Between + second : second + Between + first;

    private static string[] PairsOf(SongCopy a, SongCopy b) => [Pair(a.Mark, b.Mark), Pair(a.Source, b.Source)];

    public bool AreApart(SongCopy a, SongCopy b)
    {
        lock (_gate)
            return Marked(a) && Marked(b) && PairsOf(a, b).Any(p => _apart.Contains(p) || _unrelated.Contains(p));
    }

    public bool AreUnrelated(SongCopy a, SongCopy b)
    {
        lock (_gate)
            return Marked(a) && Marked(b) && PairsOf(a, b).Any(_unrelated.Contains);
    }

    // Grouping asks about very many pairs, nearly all of songs the user never said a word about: those are told apart at
    // once, without making the pair's key.
    private HashSet<string>? _named;

    private bool Marked(SongCopy copy)
    {
        _named ??= _apart.Concat(_unrelated).SelectMany(p => p.Split(Between)).ToHashSet(StringComparer.Ordinal);
        return _named.Contains(copy.Mark) || _named.Contains(copy.Source);
    }

    /// <summary>No two of these are duplicates of each other.</summary>
    public IReadOnlyList<string> KeepApart(IReadOnlyList<SongCopy> copies)
    {
        var added = new List<string>();
        lock (_gate)
        {
            for (var i = 0; i < copies.Count; i++)
                for (var j = i + 1; j < copies.Count; j++)
                    foreach (var pair in PairsOf(copies[i], copies[j]))
                        if (_apart.Add(pair))
                            added.Add(pair);
        }

        SaveAndTell();
        return added;
    }

    /// <summary>Takes back what <see cref="KeepApart"/> added.</summary>
    public void Rejoin(IEnumerable<string> added)
    {
        lock (_gate)
            _apart.ExceptWith(added);
        SaveAndTell();
    }

    public void Unrelate(SongCopy a, SongCopy b)
    {
        lock (_gate)
            _unrelated.UnionWith(PairsOf(a, b));
        SaveAndTell();
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _apart.Count + _unrelated.Count;
        }
    }

    /// <summary>A file was renamed: what was said about it follows.</summary>
    public void Move(string from, string to)
    {
        var any = false;
        lock (_gate)
        {
            foreach (var set in new[] { _apart, _unrelated })
            {
                foreach (var pair in set.ToList())
                {
                    var parts = pair.Split(Between);
                    if (parts.Length != 2 || parts[0] != from && parts[1] != from)
                        continue;
                    set.Remove(pair);
                    set.Add(Pair(parts[0] == from ? to : parts[0], parts[1] == from ? to : parts[1]));
                    any = true;
                }
            }
        }

        if (any)
            SaveAndTell();
    }

    /// <summary>Forgets everything that was said.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _apart.Clear();
            _unrelated.Clear();
        }

        SaveAndTell();
    }

    private void SaveAndTell()
    {
        lock (_gate)
            _named = null;
        if (_path is not null)
        {
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                    SafeFiles.WriteDurably(_path, JsonSerializer.SerializeToUtf8Bytes(new Model(1, [.. _apart], [.. _unrelated])));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
