using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Pardeh.Core.Changes;

namespace Pardeh.Core.Downloads;

/// <summary>
/// What the app keeps about downloads: a folder of its own with the unfinished ones (the part that has come and a small
/// file about it), and a list of the finished ones with their sources and licences.
/// </summary>
public sealed partial class DownloadStore
{
    private const int KeptRecords = 2000;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _records;
    private readonly Lock _gate = new();
    private readonly List<DownloadRecord> _finished = [];

    // Unfinished downloads, also those that are not written to disk.
    private readonly Dictionary<string, DownloadJob> _jobs = new(StringComparer.Ordinal);

    // Only files the store itself names are ever removed from its folder.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(pardehpart|job\.json|job\.json\.tmp|(check|tag)\.[a-z0-9]+)$")]
    private static partial Regex Ours();

    public DownloadStore(string workFolder, string recordsPath)
    {
        WorkFolder = workFolder;
        _records = recordsPath;
        try
        {
            if (File.Exists(_records) && JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllBytes(_records), Json) is { } records)
                _finished.AddRange(records.Where(r => r is { Id: not null, Source: not null, FileName: not null, Address: not null }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }

        Tidy();
    }

    public string WorkFolder { get; }

    public string PartialPath(string id) => Path.Combine(WorkFolder, id + ".pardehpart");

    private string JobPath(string id) => Path.Combine(WorkFolder, id + ".job.json");

    // Reads the unfinished downloads, and clears away what a stopped app left behind.
    private void Tidy()
    {
        try
        {
            if (!Directory.Exists(WorkFolder))
                return;
            foreach (var file in Directory.GetFiles(WorkFolder, "*.job.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<DownloadJob>(File.ReadAllBytes(file), Json) is { Id: not null, Address: not null, Host: not null, Stem: not null, Extension: not null } job
                        && Path.GetFileName(file) == job.Id + ".job.json" && Ours().IsMatch(Path.GetFileName(file)))
                        _jobs[job.Id] = job with { Stem = DownloadNames.Stem(job.Stem + ".x") };
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
                {
                }
            }

            foreach (var file in Directory.GetFiles(WorkFolder))
            {
                var name = Path.GetFileName(file);
                if (Ours().IsMatch(name) && !(_jobs.ContainsKey(name[..32]) && name[32..] is ".pardehpart" or ".job.json"))
                    SafeFiles.TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<DownloadJob> Unfinished
    {
        get
        {
            lock (_gate)
                return [.. _jobs.Values.OrderByDescending(j => j.Created)];
        }
    }

    public DownloadJob? Job(string id)
    {
        lock (_gate)
            return _jobs.GetValueOrDefault(id);
    }

    public void Keep(DownloadJob job)
    {
        lock (_gate)
        {
            _jobs[job.Id] = job;
            try
            {
                Directory.CreateDirectory(WorkFolder);
                if (job.MayBeSaved)
                    SafeFiles.WriteDurably(JobPath(job.Id), JsonSerializer.SerializeToUtf8Bytes(job, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // It can still be finished now; it just can't be gone on with after a restart.
            }
        }
    }

    /// <summary>Forgets an unfinished download and deletes what had come of it.</summary>
    public void Discard(string id)
    {
        lock (_gate)
        {
            _jobs.Remove(id);
            SafeFiles.TryDelete(PartialPath(id));
            SafeFiles.TryDelete(JobPath(id));
        }
    }

    /// <summary>Finished downloads, newest first.</summary>
    public IReadOnlyList<DownloadRecord> Finished
    {
        get
        {
            lock (_gate)
                return [.. Enumerable.Reverse(_finished)];
        }
    }

    public void Add(DownloadRecord record)
    {
        lock (_gate)
        {
            _finished.Add(record);
            if (_finished.Count > KeptRecords)
                _finished.RemoveRange(0, _finished.Count - KeptRecords);
            Save();
        }
    }

    /// <summary>Takes a download off the list. The file stays.</summary>
    public void Forget(string id)
    {
        lock (_gate)
            if (_finished.RemoveAll(r => r.Id == id) > 0)
                Save();
    }

    /// <summary>A file was renamed or moved by the app: its source and licence go with it.</summary>
    public void Moved(string from, string to)
    {
        lock (_gate)
        {
            var any = false;
            for (var i = 0; i < _finished.Count; i++)
                if (string.Equals(_finished[i].Source, from, StringComparison.Ordinal))
                {
                    _finished[i] = _finished[i] with { Source = to };
                    any = true;
                }

            if (any)
                Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_records))!);
            SafeFiles.WriteDurably(_records, JsonSerializer.SerializeToUtf8Bytes(_finished, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
