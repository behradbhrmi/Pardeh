namespace Pardeh.Core.Downloads;

public enum DownloadStage
{
    Downloading,

    /// <summary>All of it has come and is being checked.</summary>
    Checking,

    Adding,

    /// <summary>Stopped by the user or by closing the app; what has come is kept.</summary>
    Paused,

    Failed,

    Done,
}

/// <param name="Message">Why it failed, or something the user should know.</param>
public sealed record DownloadStatus(DownloadJob Job, DownloadStage Stage, long Done, long? Total, string? Message = null)
{
    public bool IsRunning => Stage is DownloadStage.Downloading or DownloadStage.Checking or DownloadStage.Adding;

    /// <summary>Whether what has come is kept, to go on with.</summary>
    public bool CanResume { get; init; }
}

/// <summary>Runs downloads, one at a time, only while the app is open.</summary>
public sealed class DownloadManager(DownloadEngine engine, DownloadStore store, IDownloadTarget target)
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _stop;
    private bool _discard;
    private long _reported;

    public DownloadEngine Engine => engine;

    public DownloadStore Store => store;

    public IDownloadTarget Target => target;

    /// <summary>The download that runs, or the last one that did; null before any.</summary>
    public DownloadStatus? Status { get; private set; }

    public bool IsBusy
    {
        get
        {
            lock (_gate)
                return _stop is not null;
        }
    }

    /// <summary>Ends when the running download does.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Raised (on any thread) as bytes come and when the stage changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised (on any thread) when a file has been added.</summary>
    public event EventHandler<DownloadRecord>? Added;

    private void Report(DownloadStatus status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts a new download or goes on with an unfinished one. False while another runs.</summary>
    public bool Start(DownloadJob job)
    {
        CancellationToken cancel;
        lock (_gate)
        {
            if (_stop is not null)
                return false;
            _stop = new CancellationTokenSource();
            cancel = _stop.Token;
            _discard = false;
        }

        store.Keep(job);
        Report(new DownloadStatus(job, DownloadStage.Downloading, PartLength(job.Id), job.Size));
        Completion = Task.Run(() => RunAsync(job, cancel));
        return true;
    }

    /// <summary>Stops and keeps what has come.</summary>
    public void Pause()
    {
        lock (_gate)
            _stop?.Cancel();
    }

    /// <summary>Stops the running download, or drops an unfinished one, and deletes what had come.</summary>
    public void Cancel(string id)
    {
        lock (_gate)
        {
            if (_stop is not null && Status?.Job.Id == id)
            {
                _discard = true;
                _stop.Cancel();
                return;
            }
        }

        store.Discard(id);
        if (Status?.Job.Id == id)
            Status = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private long PartLength(string id)
    {
        try
        {
            var file = new FileInfo(store.PartialPath(id));
            return file.Exists ? file.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    // Not through Progress<T>: its reports are posted, and a late one could follow the end.
    private sealed class Reports(Action<DownloadBytes> take) : IProgress<DownloadBytes>
    {
        public void Report(DownloadBytes value) => take(value);
    }

    private async Task RunAsync(DownloadJob job, CancellationToken cancel)
    {
        var partial = store.PartialPath(job.Id);
        DownloadStatus end;
        DownloadRecord? record = null;
        try
        {
            string? note;
            (job, note) = await engine.FetchAsync(job, partial, changed =>
            {
                job = changed;
                store.Keep(changed);
            }, new Reports(bytes =>
            {
                // A few reports a second are plenty for a bar.
                if (bytes.Done != bytes.Total && Environment.TickCount64 - _reported < 150)
                    return;
                _reported = Environment.TickCount64;
                Report(new DownloadStatus(job, DownloadStage.Downloading, bytes.Done, bytes.Total));
            }), cancel).ConfigureAwait(false);

            var length = PartLength(job.Id);
            Report(new DownloadStatus(job, DownloadStage.Checking, length, job.Size, note));
            record = await engine.CompleteAsync(job, partial, store.WorkFolder, target,
                () => Report(new DownloadStatus(job, DownloadStage.Adding, length, job.Size, note)), cancel).ConfigureAwait(false);
            store.Add(record);
            store.Discard(job.Id);
            end = new DownloadStatus(job, DownloadStage.Done, length, job.Size, note);
        }
        catch (OperationCanceledException)
        {
            end = Stopped(job, DownloadStage.Paused, null, keep: !_discard);
        }
        catch (DownloadException ex)
        {
            end = Stopped(job, DownloadStage.Failed, ex.Message, keep: ex.CanResume && !_discard);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            end = Stopped(job, DownloadStage.Failed, "The file couldn't be written: " + ex.Message, keep: !_discard);
        }

        var discarded = _discard;
        lock (_gate)
        {
            _stop?.Dispose();
            _stop = null;
        }

        if (record is not null)
            Added?.Invoke(this, record);
        if (discarded && record is null)
        {
            Status = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Report(end);
        }
    }

    private DownloadStatus Stopped(DownloadJob job, DownloadStage stage, string? message, bool keep)
    {
        var done = PartLength(job.Id);
        if (!keep)
            store.Discard(job.Id);
        return new DownloadStatus(job, stage, done, job.Size, message) { CanResume = keep };
    }
}
