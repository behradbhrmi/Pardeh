using Pardeh.Core.Changes;

namespace Pardeh.Core.OnlineInfo;

/// <summary>
/// "Check the playing song": one look-up for a song that has played for a little while, and none when a result is saved for
/// it. It changes nothing. A failure is quiet, and asking stops for a while.
/// </summary>
/// <param name="enabled">The user's settings, asked again after the wait.</param>
/// <param name="due">The song to look up, or null when it is not one to ask about.</param>
public sealed class PlayingSongCheck(SongChecker checker, Func<bool> enabled, Func<string, SongToCheck?> due, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>A song skipped past is not sent anywhere.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(10);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private CancellationTokenSource? _current;
    private string? _source;
    private DateTime _quietUntil;

    /// <summary>The look-up last begun, for tests to wait on.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Tells which song is playing, or that none is wanted (null). Telling the same one again changes nothing. Call it on one
    /// thread; the songs are asked for on it.
    /// </summary>
    public void Playing(string? source)
    {
        if (source == _source)
            return;
        _source = source;
        _current?.Cancel();
        _current = null;
        if (source is null || !enabled() || checker.Store.Now < _quietUntil)
            return;
        var mine = _current = new CancellationTokenSource();
        Completion = Look(source, mine.Token);
    }

    private async Task Look(string source, CancellationToken cancel)
    {
        try
        {
            await _delay(Settle, cancel);
            if (!enabled() || due(source) is not { } song)
                return;
            await checker.CheckAsync(song, cancel);
        }
        catch (OperationCanceledException)
        {
        }
        catch (MetadataException)
        {
            _quietUntil = checker.Store.Now + AfterFailure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
        {
            // A file that can't be read is left alone.
        }
    }
}
