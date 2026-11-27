using System.Text.Json;
using System.Text.Json.Serialization;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;

namespace Pardeh.Core.OnlineInfo;

/// <summary>What a look-up in the history came to.</summary>
public enum HistoryOutcome
{
    NoMatch,

    /// <summary>A match sure enough for a batch.</summary>
    Strong,

    /// <summary>Matches the user has to look at.</summary>
    Review,

    /// <summary>The song's details already agreed with the match.</summary>
    Right,

    /// <summary>A search typed by hand that found something.</summary>
    Found,
}

/// <summary>One thing a look-up found: enough to read it, and to open it again.</summary>
/// <param name="Id">The service's own id.</param>
public sealed record HistoryMatch(string Id, string Title, string Artist, string Album, uint Year, int LengthMs)
{
    /// <summary>The links found for it, or null when they were never asked for.</summary>
    public IReadOnlyList<SongLink>? Links { get; init; }

    public static HistoryMatch From(MetadataCandidate candidate) => new(candidate.Id, candidate.Title, candidate.ArtistText,
        MetadataMatcher.Releases(candidate, string.Empty).FirstOrDefault()?.Title ?? string.Empty, candidate.FirstYear, candidate.LengthMs);
}

/// <summary>One look-up.</summary>
/// <param name="Service">Who was asked.</param>
/// <param name="SearchedTitle">The words sent (the last ones, where several searches were tried).</param>
public sealed record LookupHistoryRow(string Service, string SearchedTitle, string? SearchedArtist, HistoryOutcome Outcome, IReadOnlyList<HistoryMatch> Matches)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DateTime When { get; init; }

    /// <summary>The song's title and artist then; empty for a search typed by hand.</summary>
    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    /// <summary>The file looked up, or null for a search typed by hand.</summary>
    public string? Source { get; init; }

    public string FileName { get; init; } = string.Empty;
}

/// <summary>
/// The look-ups made, kept on this device only: what was searched, where, and what came back. Unlike
/// <see cref="OnlineStore"/> it does not forget a song when its file changes. One JSON file (null: memory only).
/// </summary>
public sealed class LookupHistory
{
    public const int MaxRows = 5000;
    public const int KeptMatches = 5;

    private const int FileVersion = 1;
    private const int SaveEvery = 10;

    private sealed record Model(int Version, bool Seeded, List<LookupHistoryRow> Rows);

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string? _path;
    private readonly Func<DateTime> _now;
    private readonly Func<bool> _keep;
    private readonly Lock _gate = new();

    // Oldest first: the new ones go at the end, the ones dropped come off the front.
    private readonly List<LookupHistoryRow> _rows = [];
    private bool _seeded;
    private int _unsaved;
    private DateTime _saved;

    /// <param name="keep">The user's switch: while it is off nothing is written down.</param>
    public LookupHistory(string? path = null, Func<bool>? keep = null, Func<DateTime>? now = null)
    {
        _path = path;
        _keep = keep ?? (() => true);
        _now = now ?? (() => DateTime.UtcNow);
        _saved = _now();
        try
        {
            if (path is not null && File.Exists(path) && JsonSerializer.Deserialize<Model>(File.ReadAllBytes(path), Json) is { Version: FileVersion, Rows: { } rows } model)
            {
                _seeded = model.Seeded;
                _rows.AddRange(rows.Where(r => r is { Id: not null, Service: not null, SearchedTitle: not null, Matches: not null }).TakeLast(MaxRows));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // An unreadable history starts empty.
        }
    }

    /// <summary>Raised (on any thread) when rows are added, changed or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<LookupHistoryRow> Rows
    {
        get
        {
            lock (_gate)
                return [.. Enumerable.Reverse(_rows)];
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _rows.Count;
        }
    }

    public LookupHistoryRow? Find(string id)
    {
        lock (_gate)
            return _rows.FirstOrDefault(r => r.Id == id);
    }

    /// <summary>Writes a look-up down, unless the user keeps no history.</summary>
    public void Add(LookupHistoryRow row)
    {
        if (!_keep())
            return;
        bool save;
        lock (_gate)
        {
            _rows.Add(row with { When = row.When == default ? _now() : row.When, Matches = [.. row.Matches.Take(KeptMatches)] });
            if (_rows.Count > MaxRows)
                _rows.RemoveRange(0, _rows.Count - MaxRows);
            save = ++_unsaved >= SaveEvery || _now() - _saved >= TimeSpan.FromSeconds(10);
        }

        if (save)
            Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A look-up of a library file.</summary>
    public void AddSong(string service, string source, OnlineEntry entry)
    {
        if (entry.Searched is not { } searched)
            return;
        Add(new LookupHistoryRow(service, searched.Title, searched.Artist, OutcomeOf(entry.Group), [.. entry.Candidates.Take(KeptMatches).Select(HistoryMatch.From)])
        {
            When = entry.LookedUp,
            Title = entry.Song.Title,
            Artist = string.Join("; ", entry.Song.Artists),
            Source = source,
            FileName = MediaSources.DisplayName(source),
        });
    }

    /// <summary>A search typed by hand, for a library file or for any song.</summary>
    public void AddSearch(string service, string title, string? artist, IReadOnlyList<HistoryMatch> matches, string? source = null) =>
        Add(new LookupHistoryRow(service, title, artist, matches.Count > 0 ? HistoryOutcome.Found : HistoryOutcome.NoMatch, matches)
        {
            Source = source,
            FileName = source is null ? string.Empty : MediaSources.DisplayName(source),
        });

    /// <summary>A match's own page at the service, or null when there is none that is safe to open.</summary>
    public static Uri? PageOf(string service, HistoryMatch match) =>
        service == MusicBrainzName && Guid.TryParse(match.Id, out var id) ? new Uri($"https://musicbrainz.org/recording/{id:D}") : null;

    private const string MusicBrainzName = "MusicBrainz";

    private static HistoryOutcome OutcomeOf(OnlineGroup group) => group switch
    {
        OnlineGroup.Strong => HistoryOutcome.Strong,
        OnlineGroup.Review => HistoryOutcome.Review,
        OnlineGroup.Checked => HistoryOutcome.Right,
        _ => HistoryOutcome.NoMatch,
    };

    /// <summary>Takes in, once, the results saved before there was a history.</summary>
    public void Seed(string service, IEnumerable<KeyValuePair<string, OnlineEntry>> saved)
    {
        lock (_gate)
            if (_seeded || !_keep())
                return;
        foreach (var (source, entry) in saved.OrderBy(s => s.Value.LookedUp))
            AddSong(service, source, entry);
        lock (_gate)
        {
            _seeded = true;
            _unsaved++;
        }

        Save();
    }

    /// <summary>A match of one row, changed (its links, once asked for).</summary>
    public void Update(string id, Func<LookupHistoryRow, LookupHistoryRow> change)
    {
        lock (_gate)
        {
            var at = _rows.FindIndex(r => r.Id == id);
            if (at < 0)
                return;
            _rows[at] = change(_rows[at]);
            _unsaved++;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Links were asked for: every row that found that recording now has them.</summary>
    public void SetLinks(string service, string matchId, IReadOnlyList<SongLink> links)
    {
        var any = false;
        lock (_gate)
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].Service == service && _rows[i].Matches.Any(m => m.Id == matchId))
                {
                    _rows[i] = _rows[i] with { Matches = [.. _rows[i].Matches.Select(m => m.Id == matchId ? m with { Links = links } : m)] };
                    any = true;
                }

        if (!any)
            return;
        lock (_gate)
            _unsaved++;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool HasLinks(LookupHistoryRow row) => row.Matches.Any(m => m.Links is { Count: > 0 });

    /// <summary>A file was renamed or moved by the app: its rows go on pointing at it.</summary>
    public void Moved(string from, string to)
    {
        var any = false;
        lock (_gate)
            for (var i = 0; i < _rows.Count; i++)
                if (string.Equals(_rows[i].Source, from, StringComparison.Ordinal))
                {
                    _rows[i] = _rows[i] with { Source = to };
                    any = true;
                }

        if (!any)
            return;
        lock (_gate)
            _unsaved++;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            if (_rows.RemoveAll(r => r.Id == id) == 0)
                return;
            _unsaved++;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes every row. Results saved elsewhere are not taken in again.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _rows.Clear();
            _seeded = true;
            _unsaved++;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The rows that have every word typed, newest first.</summary>
    public static IEnumerable<LookupHistoryRow> Filter(IEnumerable<LookupHistoryRow> rows, string? text, HistoryOutcome? outcome = null,
        string? service = null, Func<LookupHistoryRow, bool>? also = null)
    {
        // Typed without accents, a name with them is still found.
        static string Plain(string? value) => string.Concat(TextFold.Key(value).Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark));
        var words = Plain(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var row in rows)
        {
            if (outcome is { } wanted && row.Outcome != wanted || service is not null && row.Service != service || also is not null && !also(row))
                continue;
            if (words.Length > 0)
            {
                var all = Plain(string.Join(' ', new[] { row.SearchedTitle, row.SearchedArtist, row.Title, row.Artist, row.FileName }
                    .Concat(row.Matches.SelectMany(m => new[] { m.Title, m.Artist, m.Album }))));
                if (!words.All(w => all.Contains(w, StringComparison.Ordinal)))
                    continue;
            }

            yield return row;
        }
    }

    /// <summary>Writes what has not been written yet.</summary>
    public void Flush()
    {
        lock (_gate)
            if (_unsaved == 0)
                return;
        Save();
    }

    // Under the lock to the end: two saves at once would write the same temporary file.
    private void Save()
    {
        lock (_gate)
        {
            _saved = _now();
            if (_path is null)
            {
                _unsaved = 0;
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                SafeFiles.WriteDurably(_path, JsonSerializer.SerializeToUtf8Bytes(new Model(FileVersion, _seeded, _rows), Json));
                _unsaved = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again with the next change.
            }
        }
    }
}
