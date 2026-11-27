using System.Diagnostics;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>A song to look up, with what the library knows of the file.</summary>
/// <param name="Seconds">Its length, or 0 when unknown.</param>
public sealed record SongToCheck(string Source, long Size, DateTime Modified, double Seconds);

/// <summary>A song's tags now and the local checks of it.</summary>
public sealed record SongFacts(CleanupProposal Local, TagFields Tags, JunkRules Rules);

/// <summary>
/// Looks one song up and saves what that comes to. Nothing is changed in the file.
/// </summary>
/// <param name="read">The song's tags and local checks, or null when the file can't be read.</param>
/// <param name="databaseScript">The user's choice to take names in the database's script.</param>
public sealed class SongChecker(MetadataLookup lookup, OnlineStore store, Func<string, Task<SongFacts?>> read, Func<bool> databaseScript)
{
    public OnlineStore Store => store;

    /// <summary>
    /// The saved result, or null when the file can't be read or has nothing to search for. Throws
    /// <see cref="MetadataException"/> when the service could not be asked: nothing is saved then.
    /// </summary>
    public async Task<OnlineEntry?> CheckAsync(SongToCheck song, CancellationToken cancel)
    {
        if (await read(song.Source).ConfigureAwait(false) is not { } facts)
            return null;
        var steps = MetadataLookup.Steps(facts.Tags, facts.Local, song.Seconds, facts.Rules);
        if (steps.Count == 0)
            return null;
        var result = await lookup.FindAsync(steps, cancel).ConfigureAwait(false);
        return Save(song, facts, steps, result);
    }

    /// <summary>Saves the result of a look-up of these steps.</summary>
    public OnlineEntry Save(SongToCheck song, SongFacts facts, IReadOnlyList<LookupStep> steps, LookupResult result)
    {
        var read = (result.Matches.Count > 0 ? steps.FirstOrDefault(s => s.Search == result.Searched) : null)?.Song ?? steps[0].Song;
        var verdict = OnlineJudge.Judge(facts.Local, facts.Tags, read, result.Matches, databaseScript());
        var entry = new OnlineEntry
        {
            Size = song.Size,
            Modified = song.Modified,
            LookedUp = store.Now,
            Matcher = OnlineStore.MatcherVersion,
            Group = verdict.Group,
            Song = read,
            Searched = result.Searched,
            Candidates = [.. result.Matches.Select(m => m.Candidate)],
            Fields = [.. verdict.Proposal.Suggestions.Where(s => s.Selected && verdict.Group == OnlineGroup.Strong).Select(s => s.Field).Distinct()],
            Why = verdict.Why,
            WantsCover = verdict.WantsCover,
        };
        store.Put(song.Source, entry);
        return entry;
    }
}

public enum CheckState
{
    Idle,
    Running,

    /// <summary>The service is busy or out of reach: it is tried again shortly.</summary>
    Waiting,

    Paused,

    /// <summary>Went through every song.</summary>
    Done,

    /// <summary>Stopped before the end. What was found is kept.</summary>
    Stopped,
}

/// <param name="Done">Songs finished with, those that failed or could not be searched too.</param>
/// <param name="Failed">Songs the service could not be asked about. They are asked about next time.</param>
/// <param name="Skipped">Songs that can't be read, or have no title to search for.</param>
/// <param name="Message">Why it waits or paused.</param>
/// <param name="Remaining">A guess from the songs so far, or null.</param>
public sealed record CheckProgress(CheckState State, int Total, int Done, int Failed, int Skipped, string? Message = null, TimeSpan? Remaining = null)
{
    public bool IsActive => State is CheckState.Running or CheckState.Waiting or CheckState.Paused;
}

/// <summary>
/// "Check my library": looks songs up one after another, because the user asked. It can be paused and stopped; what it found
/// is saved, so starting again goes on where it was. When the service keeps failing it waits longer, and then pauses.
/// </summary>
/// <param name="allowed">Whether online look-ups are still on: turning them off ends the check.</param>
public sealed class LibraryCheck(SongChecker checker, Func<bool> allowed, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>A song takes one to four requests, a second apart at least, and longer when the service is busy.</summary>
    public const double FastestSeconds = 1.5, SlowestSeconds = 6;

    // After one failure, two, three. The fourth in a row pauses.
    private static readonly TimeSpan[] Waits = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];
    private const int TriesPerSong = 2;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _stop;
    private TaskCompletionSource? _resume;
    private bool _paused;
    private int _held;
    private string? _pauseReason;

    private readonly HashSet<SongToCheck> _passedOver = [];

    public CheckProgress Progress { get; private set; } = new(CheckState.Idle, 0, 0, 0, 0);

    /// <summary>Whether a check has found nothing to ask about for the file as it is: it is not offered again this time.</summary>
    public bool WasPassedOver(SongToCheck song)
    {
        lock (_gate)
            return _passedOver.Contains(song);
    }

    /// <summary>Raised (on any thread) as songs are finished and when the state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Ends when the run does.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>How long looking up this many songs is likely to take, at the quickest and the slowest.</summary>
    public static (TimeSpan Fastest, TimeSpan Slowest) Estimate(int songs) =>
        (TimeSpan.FromSeconds(songs * FastestSeconds), TimeSpan.FromSeconds(songs * SlowestSeconds));

    /// <summary>Starts on these songs, in this order. Nothing happens while a run is on, or with no songs.</summary>
    public void Start(IReadOnlyList<SongToCheck> songs)
    {
        CancellationToken cancel;
        lock (_gate)
        {
            if (_stop is not null || songs.Count == 0)
                return;
            _stop = new CancellationTokenSource();
            cancel = _stop.Token;
            _paused = false;
            _pauseReason = null;
            Progress = new CheckProgress(CheckState.Running, songs.Count, 0, 0, 0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        var list = songs.ToList();
        Completion = Task.Run(() => RunAsync(list, cancel));
    }

    /// <summary>Waits after the song being looked up.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_stop is null || _paused)
                return;
            _paused = true;
            _pauseReason = null;
        }

        Report(Progress with { State = CheckState.Paused, Message = null });
    }

    public void Resume()
    {
        TaskCompletionSource? waiting;
        lock (_gate)
        {
            if (_stop is null || !_paused)
                return;
            _paused = false;
            _pauseReason = null;
            waiting = _resume;
            _resume = null;
        }

        Report(Progress with { State = CheckState.Running, Message = null });
        waiting?.TrySetResult();
    }

    /// <summary>Gives up on the song being looked up and ends the run.</summary>
    public void Stop()
    {
        lock (_gate)
            _stop?.Cancel();
    }

    /// <summary>Holds the check while files are being changed; dispose to go on.</summary>
    public IDisposable Hold()
    {
        Interlocked.Increment(ref _held);
        return new Release(this);
    }

    private sealed class Release(LibraryCheck check) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                Interlocked.Decrement(ref check._held);
        }
    }

    private void Report(CheckProgress progress)
    {
        Progress = progress;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancel)
    {
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            Task? resumed = null;
            lock (_gate)
            {
                if (_paused)
                    resumed = (_resume ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            if (resumed is not null)
            {
                checker.Store.Flush(); // the app may be closed while it waits
                if (Progress.State != CheckState.Paused)
                    Report(Progress with { State = CheckState.Paused, Message = _pauseReason, Remaining = null });
                await resumed.WaitAsync(cancel).ConfigureAwait(false);
            }
            else if (Volatile.Read(ref _held) > 0)
            {
                await Task.Delay(100, cancel).ConfigureAwait(false);
            }
            else
            {
                return;
            }
        }
    }

    private async Task RunAsync(List<SongToCheck> songs, CancellationToken cancel)
    {
        var end = CheckState.Stopped;
        string? why = null;
        try
        {
            int failures = 0, tries = 0, timed = 0;
            var spent = TimeSpan.Zero;
            for (var i = 0; i < songs.Count;)
            {
                await WaitWhilePausedAsync(cancel).ConfigureAwait(false);
                if (!allowed())
                {
                    why = "Online look-ups were turned off.";
                    return;
                }

                var song = songs[i];
                var clock = Stopwatch.StartNew();
                var skipped = false;
                try
                {
                    skipped = await checker.CheckAsync(song, cancel).ConfigureAwait(false) is null;
                    failures = 0;
                }
                catch (MetadataException ex)
                {
                    cancel.ThrowIfCancellationRequested();
                    failures++;
                    tries++;
                    if (failures > Waits.Length)
                    {
                        // The same song is asked about first when the user resumes.
                        lock (_gate)
                        {
                            _paused = true;
                            _pauseReason = ex.Message + " Paused: resume when you like.";
                        }

                        failures = 0;
                        tries = 0;
                        continue;
                    }

                    Report(Progress with { State = _paused ? CheckState.Paused : CheckState.Waiting, Message = ex.Message, Remaining = null });
                    await _delay(Waits[failures - 1], cancel).ConfigureAwait(false);
                    if (Progress.State == CheckState.Waiting)
                        Report(Progress with { State = CheckState.Running, Message = null });
                    if (tries < TriesPerSong)
                        continue;
                    Progress = Progress with { Failed = Progress.Failed + 1 };
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
                {
                    skipped = true;
                }

                tries = 0;
                i++;
                if (skipped)
                {
                    lock (_gate)
                        _passedOver.Add(song);
                }
                else
                {
                    spent += clock.Elapsed;
                    timed++;
                }

                // Nothing is said until a few songs show the pace.
                TimeSpan? remaining = timed >= 5 ? spent / timed * (songs.Count - i) : null;
                Report(Progress with
                {
                    State = _paused ? CheckState.Paused : CheckState.Running, Done = i, Skipped = Progress.Skipped + (skipped ? 1 : 0), Message = null,
                    Remaining = remaining,
                });
            }

            end = CheckState.Done;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            checker.Store.Flush();
            lock (_gate)
            {
                _stop?.Dispose();
                _stop = null;
                _paused = false;
                _resume = null;
            }

            Report(Progress with { State = end, Message = why, Remaining = null });
        }
    }
}
