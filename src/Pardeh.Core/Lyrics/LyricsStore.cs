using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pardeh.Core.LyricsKit;

/// <summary>The parts of what is saved for a track.</summary>
[Flags]
public enum LyricsPieces
{
    None = 0,
    Own = 1,
    Fetched = 2,
    Chords = 4,
    Offset = 8,
    Transpose = 16,
}

/// <summary>What the index remembers about one track (everything optional; an entry with nothing left is removed).</summary>
public sealed class LyricsIndexEntry
{
    public string? Title { get; set; }

    public string? Artist { get; set; }

    public string? Album { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>The user's timing correction, see <see cref="Lyrics.OffsetMs"/>.</summary>
    public int OffsetMs { get; set; }

    /// <summary>File name of the user's own lyrics (in the lyrics folder), if any.</summary>
    public string? OverrideFile { get; set; }

    /// <summary>File name of fetched lyrics (in the <c>online</c> sub folder), if any.</summary>
    public string? FetchedFile { get; set; }

    public string? Provider { get; set; }

    public DateTime? FetchedUtc { get; set; }

    /// <summary>When an online lookup last found nothing (so it is not repeated for a while).</summary>
    public DateTime? NotFoundUtc { get; set; }

    /// <summary>File name of the user's own chords text (in the lyrics folder), if any.</summary>
    public string? ChordsFile { get; set; }

    /// <summary>The user's transposition of the chords, in semitones.</summary>
    public int Transpose { get; set; }

    [JsonIgnore]
    internal bool IsEmpty => OffsetMs == 0 && OverrideFile is null && FetchedFile is null && NotFoundUtc is null && ChordsFile is null && Transpose == 0;
}

/// <summary>
/// The lyrics a user saved or Pardeh fetched, kept in the app data folder (never next to the music): <c>{hash}.lrc</c> or
/// <c>{hash}.txt</c> for the user's own text, <c>online/{hash}.lrc</c> for fetched lyrics, and a small <c>index.json</c> with the
/// per-track offset and the "not found" marks. The hash comes from <see cref="LyricsTrackKey"/>. All members are thread safe.
/// </summary>
public sealed class LyricsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
    private readonly object _gate = new();
    private readonly string _folder;
    private Dictionary<string, LyricsIndexEntry>? _index;

    public LyricsStore(string folder)
    {
        _folder = folder;
    }

    public string Folder => _folder;

    private string IndexPath => Path.Combine(_folder, "index.json");

    private string OnlineFolder => Path.Combine(_folder, "online");

    private sealed class IndexFile
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, LyricsIndexEntry> Tracks { get; set; } = [];
    }

    // The index is loaded once; every change saves it again (it is a few hundred bytes per track).
    private Dictionary<string, LyricsIndexEntry> Index
    {
        get
        {
            if (_index is not null)
                return _index;
            _index = [];
            try
            {
                if (File.Exists(IndexPath) && JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(IndexPath, Encoding.UTF8)) is { } file)
                    _index = new Dictionary<string, LyricsIndexEntry>(file.Tracks, StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged index is started again; the lyrics files themselves stay.
            }

            return _index;
        }
    }

    private void SaveIndex()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            WriteAtomic(IndexPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new IndexFile { Tracks = Index }, Json)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }

    private LyricsIndexEntry Entry(LyricsTrackKey key)
    {
        if (!Index.TryGetValue(key.Hash, out var entry))
            Index[key.Hash] = entry = new LyricsIndexEntry();
        entry.Title = key.Title;
        entry.Artist = key.Artist;
        entry.Album = key.Album;
        entry.DurationSeconds = key.DurationSeconds;
        return entry;
    }

    private void Tidy(LyricsTrackKey key)
    {
        if (Index.TryGetValue(key.Hash, out var entry) && entry.IsEmpty)
            Index.Remove(key.Hash);
    }

    private static string FileFor(string hash, Lyrics lyrics) => hash + (lyrics.Synced ? ".lrc" : ".txt");

    private static Lyrics? ReadFile(string path, string source)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > LyricsLimits.MaxBytes)
                return null;
            var lyrics = LrcParser.Parse(LyricsDecoder.Decode(File.ReadAllBytes(path)), source);
            if (lyrics is not null)
                lyrics.Source = source;
            return lyrics;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // --- the user's own lyrics -------------------------------------------------------------------------------------------------

    public Lyrics? GetOverride(LyricsTrackKey key)
    {
        lock (_gate)
        {
            return Index.TryGetValue(key.Hash, out var entry) && entry.OverrideFile is { } name ? ReadFile(Path.Combine(_folder, name), LyricsSources.Override) : null;
        }
    }

    /// <summary>Saves the user's lyrics for the track (replacing earlier ones), with their offset.</summary>
    public void SaveOverride(LyricsTrackKey key, Lyrics lyrics)
    {
        lock (_gate)
        {
            var entry = Entry(key);
            var name = FileFor(key.Hash, lyrics);
            Directory.CreateDirectory(_folder);
            WriteAtomic(Path.Combine(_folder, name), new UTF8Encoding(false).GetBytes(lyrics.ToLrc(applyOffset: false)));
            if (entry.OverrideFile is { } old && old != name)
                DeleteFile(Path.Combine(_folder, old));
            entry.OverrideFile = name;
            entry.OffsetMs = lyrics.OffsetMs;
            SaveIndex();
        }
    }

    public bool DeleteOverride(LyricsTrackKey key)
    {
        lock (_gate)
        {
            if (!Index.TryGetValue(key.Hash, out var entry) || entry.OverrideFile is not { } name)
                return false;
            DeleteFile(Path.Combine(_folder, name));
            entry.OverrideFile = null;
            Tidy(key);
            SaveIndex();
            return true;
        }
    }

    // --- the user's own chords -----------------------------------------------------------------------------------------

    /// <summary>The user's own chords text exactly as saved, or null.</summary>
    public string? GetChordsText(LyricsTrackKey key)
    {
        lock (_gate)
        {
            if (!Index.TryGetValue(key.Hash, out var entry) || entry.ChordsFile is not { } name)
                return null;
            try
            {
                var path = Path.Combine(_folder, name);
                return File.Exists(path) && new FileInfo(path).Length <= LyricsLimits.MaxBytes ? LyricsDecoder.Decode(File.ReadAllBytes(path)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void SaveChordsText(LyricsTrackKey key, string text)
    {
        lock (_gate)
        {
            var entry = Entry(key);
            var name = key.Hash + ".cho";
            Directory.CreateDirectory(_folder);
            WriteAtomic(Path.Combine(_folder, name), new UTF8Encoding(false).GetBytes(text));
            entry.ChordsFile = name;
            SaveIndex();
        }
    }

    public bool DeleteChordsText(LyricsTrackKey key)
    {
        lock (_gate)
        {
            if (!Index.TryGetValue(key.Hash, out var entry) || entry.ChordsFile is not { } name)
                return false;
            DeleteFile(Path.Combine(_folder, name));
            entry.ChordsFile = null;
            Tidy(key);
            SaveIndex();
            return true;
        }
    }

    public int GetTranspose(LyricsTrackKey key)
    {
        lock (_gate)
        {
            return Index.TryGetValue(key.Hash, out var entry) ? entry.Transpose : 0;
        }
    }

    public void SetTranspose(LyricsTrackKey key, int semitones)
    {
        lock (_gate)
        {
            if (semitones == 0 && !Index.ContainsKey(key.Hash))
                return;
            Entry(key).Transpose = semitones;
            Tidy(key);
            SaveIndex();
        }
    }

    // --- fetched lyrics --------------------------------------------------------------------------------------------------------

    public Lyrics? GetFetched(LyricsTrackKey key)
    {
        lock (_gate)
        {
            return Index.TryGetValue(key.Hash, out var entry) && entry.FetchedFile is { } name
                ? ReadFile(Path.Combine(OnlineFolder, name), LyricsSources.OnlineCache)
                : null;
        }
    }

    public void SaveFetched(LyricsTrackKey key, Lyrics lyrics, string provider, DateTime nowUtc)
    {
        lock (_gate)
        {
            var entry = Entry(key);
            var name = FileFor(key.Hash, lyrics);
            Directory.CreateDirectory(OnlineFolder);
            WriteAtomic(Path.Combine(OnlineFolder, name), new UTF8Encoding(false).GetBytes(lyrics.ToLrc(applyOffset: false)));
            if (entry.FetchedFile is { } old && old != name)
                DeleteFile(Path.Combine(OnlineFolder, old));
            entry.FetchedFile = name;
            entry.Provider = provider;
            entry.FetchedUtc = nowUtc;
            entry.NotFoundUtc = null;
            SaveIndex();
        }
    }

    /// <summary>Deletes every lyrics text the user saved (Settings > Lyrics > "Delete my edited lyrics"); fetched lyrics and offsets stay. Returns how many were deleted.</summary>
    public int ClearOverrides()
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var (hash, entry) in Index.ToList())
            {
                if (entry.OverrideFile is not { } name)
                    continue;
                DeleteFile(Path.Combine(_folder, name));
                entry.OverrideFile = null;
                count++;
                if (entry.IsEmpty)
                    Index.Remove(hash);
            }

            if (count > 0)
                SaveIndex();
            return count;
        }
    }

    /// <summary>Deletes all fetched lyrics and "not found" marks (the user's own lyrics and offsets stay).</summary>
    public void ClearFetched()
    {
        lock (_gate)
        {
            try
            {
                if (Directory.Exists(OnlineFolder))
                    Directory.Delete(OnlineFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            foreach (var (hash, entry) in Index.ToList())
            {
                entry.FetchedFile = null;
                entry.Provider = null;
                entry.FetchedUtc = null;
                entry.NotFoundUtc = null;
                if (entry.IsEmpty)
                    Index.Remove(hash);
            }

            SaveIndex();
        }
    }

    // --- a track whose tags or name changed --------------------------------------------------------------------------------------

    /// <summary>What is saved under a key.</summary>
    public LyricsPieces PiecesOf(LyricsTrackKey key)
    {
        lock (_gate)
        {
            if (!Index.TryGetValue(key.Hash, out var entry))
                return LyricsPieces.None;
            return (entry.OverrideFile is null ? LyricsPieces.None : LyricsPieces.Own)
                | (entry.FetchedFile is null ? LyricsPieces.None : LyricsPieces.Fetched)
                | (entry.ChordsFile is null ? LyricsPieces.None : LyricsPieces.Chords)
                | (entry.OffsetMs == 0 ? LyricsPieces.None : LyricsPieces.Offset)
                | (entry.Transpose == 0 ? LyricsPieces.None : LyricsPieces.Transpose);
        }
    }

    /// <summary>
    /// What a <see cref="Transfer"/> from one track key to another would move: only what the old key has and the new one has
    /// not, so that lyrics already saved under the new key are never replaced. A "not found" mark is not moved: new details
    /// deserve a new look-up.
    /// </summary>
    public LyricsPieces PlanTransfer(LyricsTrackKey from, LyricsTrackKey to)
    {
        lock (_gate)
        {
            if (from.Hash == to.Hash || !Index.TryGetValue(from.Hash, out var old))
                return LyricsPieces.None;
            Index.TryGetValue(to.Hash, out var target);
            var pieces = LyricsPieces.None;
            if (old.OverrideFile is not null && target?.OverrideFile is null)
                pieces |= LyricsPieces.Own;
            if (old.FetchedFile is not null && target?.FetchedFile is null)
                pieces |= LyricsPieces.Fetched;
            if (old.ChordsFile is not null && target?.ChordsFile is null)
                pieces |= LyricsPieces.Chords;
            if (old.OffsetMs != 0 && (target?.OffsetMs ?? 0) == 0)
                pieces |= LyricsPieces.Offset;
            if (old.Transpose != 0 && (target?.Transpose ?? 0) == 0)
                pieces |= LyricsPieces.Transpose;
            return pieces;
        }
    }

    /// <summary>
    /// Carries <paramref name="pieces"/> from one key to another. With <paramref name="keepOld"/> (another track still has
    /// the old key) they are copied. Safe to run twice. Throws when a file can't be copied; nothing is lost then.
    /// </summary>
    public void Transfer(LyricsTrackKey from, LyricsTrackKey to, LyricsPieces pieces, bool keepOld)
    {
        lock (_gate)
        {
            if (pieces == LyricsPieces.None || from.Hash == to.Hash || !Index.TryGetValue(from.Hash, out var old))
                return;
            var entry = Entry(to);
            var moved = new List<string>();

            string? Copy(string? name, string folder)
            {
                if (name is null)
                    return null;
                var source = Path.Combine(folder, name);
                if (!File.Exists(source))
                    return null;
                var copy = to.Hash + Path.GetExtension(name);
                File.Copy(source, Path.Combine(folder, copy), overwrite: true); // the new key has no such piece of its own
                moved.Add(source);
                return copy;
            }

            if (pieces.HasFlag(LyricsPieces.Own) && Copy(old.OverrideFile, _folder) is { } own)
                entry.OverrideFile = own;
            if (pieces.HasFlag(LyricsPieces.Fetched) && Copy(old.FetchedFile, OnlineFolder) is { } fetched)
            {
                entry.FetchedFile = fetched;
                entry.Provider = old.Provider;
                entry.FetchedUtc = old.FetchedUtc;
                entry.NotFoundUtc = null;
            }

            if (pieces.HasFlag(LyricsPieces.Chords) && Copy(old.ChordsFile, _folder) is { } chords)
                entry.ChordsFile = chords;
            if (pieces.HasFlag(LyricsPieces.Offset) && old.OffsetMs != 0)
                entry.OffsetMs = old.OffsetMs;
            if (pieces.HasFlag(LyricsPieces.Transpose) && old.Transpose != 0)
                entry.Transpose = old.Transpose;

            if (!keepOld)
            {
                if (pieces.HasFlag(LyricsPieces.Own) && entry.OverrideFile is not null)
                    old.OverrideFile = null;
                if (pieces.HasFlag(LyricsPieces.Fetched) && entry.FetchedFile is not null)
                    (old.FetchedFile, old.Provider, old.FetchedUtc) = (null, null, null);
                if (pieces.HasFlag(LyricsPieces.Chords) && entry.ChordsFile is not null)
                    old.ChordsFile = null;
                if (pieces.HasFlag(LyricsPieces.Offset))
                    old.OffsetMs = 0;
                if (pieces.HasFlag(LyricsPieces.Transpose))
                    old.Transpose = 0;
            }

            Tidy(to);
            Tidy(from);
            SaveIndex();
            if (!keepOld)
                foreach (var file in moved)
                    DeleteFile(file); // only after the index points at the copies
        }
    }

    // --- offsets and "not found" marks -----------------------------------------------------------------------------------------

    public int GetOffset(LyricsTrackKey key)
    {
        lock (_gate)
        {
            return Index.TryGetValue(key.Hash, out var entry) ? entry.OffsetMs : 0;
        }
    }

    public void SetOffset(LyricsTrackKey key, int offsetMs)
    {
        lock (_gate)
        {
            if (offsetMs == 0 && !Index.ContainsKey(key.Hash))
                return;
            Entry(key).OffsetMs = offsetMs;
            Tidy(key);
            SaveIndex();
        }
    }

    public bool IsNotFound(LyricsTrackKey key, DateTime nowUtc, TimeSpan window)
    {
        lock (_gate)
        {
            return Index.TryGetValue(key.Hash, out var entry) && entry.NotFoundUtc is { } at && nowUtc - at < window && nowUtc >= at - TimeSpan.FromDays(1);
        }
    }

    /// <summary>Forgets a "not found" mark (the user asked to search again).</summary>
    public void ClearNotFound(LyricsTrackKey key)
    {
        lock (_gate)
        {
            if (!Index.TryGetValue(key.Hash, out var entry) || entry.NotFoundUtc is null)
                return;
            entry.NotFoundUtc = null;
            Tidy(key);
            SaveIndex();
        }
    }

    /// <summary>Remembers that a lookup found nothing. Only the hash and the time are kept for such a track.</summary>
    public void MarkNotFound(LyricsTrackKey key, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!Index.TryGetValue(key.Hash, out var entry))
                Index[key.Hash] = entry = new LyricsIndexEntry();
            entry.NotFoundUtc = nowUtc;
            SaveIndex();
        }
    }
}
