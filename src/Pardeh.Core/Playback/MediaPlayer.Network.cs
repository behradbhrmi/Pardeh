namespace Pardeh.Core.Playback;

/// <summary>
/// Network items: connecting happens off the interface thread and can be cancelled, a dropped stream is connected again, and
/// what is going on is reported (<see cref="NetworkPhase"/>).
/// </summary>
public sealed partial class MediaPlayer
{
    private static readonly TimeSpan[] ReconnectDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    private CancellationTokenSource? _connect;
    private bool _connectPlay;
    private int _reconnects;
    private TimeSpan _playedSinceReconnect;
    private NetworkPhase _networkPhase;
    private string _networkMessage = string.Empty;

    public NetworkOptions Network { get; set; } = new();

    /// <summary>The waits before each new try after a drop (tests shorten them).</summary>
    public IReadOnlyList<TimeSpan> ReconnectWaits { get; set; } = ReconnectDelays;

    /// <summary>True while a network item is being connected to.</summary>
    public bool IsConnecting => _connect is not null;

    /// <summary>The connection attempt in progress (finished when none is).</summary>
    public Task PendingOpen { get; private set; } = Task.CompletedTask;

    public NetworkPhase NetworkPhase => _networkPhase;

    /// <summary>One line for the interface: "Connecting...", "Buffering 40%", what went wrong.</summary>
    public string NetworkMessage => _networkMessage;

    public event EventHandler? NetworkChanged;

    private void SetNetwork(NetworkPhase phase, string message = "")
    {
        if (_networkPhase == phase && _networkMessage == message)
            return;
        _networkPhase = phase;
        _networkMessage = message;
        NetworkChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gives up a connection attempt in progress. True when there was one.</summary>
    private bool AbandonConnect()
    {
        if (_connect is not { } attempt)
            return false;
        _connect = null;
        attempt.Cancel();
        return true;
    }

    /// <summary>Cancels connecting (the banner's Cancel button, Stop).</summary>
    public void CancelConnect()
    {
        if (!AbandonConnect())
            return;
        SetNetwork(NetworkPhase.None);
        SetOpenState(OpenState.Undefined);
        SetPlayState(PlayState.Stopped);
        SetStatus("Stopped");
    }

    private void StartConnect(MediaItem item, bool play, double startAt, TimeSpan wait)
    {
        AbandonConnect();
        var attempt = _connect = new CancellationTokenSource();
        _connectPlay = play;
        var phase = _reconnects > 0 ? NetworkPhase.Reconnecting : NetworkPhase.Connecting;
        var message = _reconnects > 0 ? $"Reconnecting ({_reconnects} of {ReconnectWaits.Count})..." : "Connecting...";
        SetNetwork(phase, message);
        SetStatus(message);
        PendingOpen = ConnectAsync(item, startAt, wait, attempt);
    }

    private async Task ConnectAsync(MediaItem item, double startAt, TimeSpan wait, CancellationTokenSource attempt)
    {
        var cancel = attempt.Token;
        var options = Network;
        IDisposable? prepared = null;
        Exception? failure = null;
        try
        {
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancel);
            // Not tied to the token: a result that arrives after a cancel still has to be disposed.
            prepared = await Task.Run(() => _engine.PrepareOpen(item.Source, options, cancel), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ex;
        }

        if (cancel.IsCancellationRequested || !ReferenceEquals(_connect, attempt))
        {
            prepared?.Dispose(); // something else was asked for meanwhile
            return;
        }

        _connect = null;
        if (prepared is not null)
        {
            try
            {
                _engine.OpenPrepared(prepared);
                ApplyReplayGain();
                SetNetwork(NetworkPhase.None);
                _playedSinceReconnect = TimeSpan.Zero;
                FinishOpen(item, _connectPlay, startAt);
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                prepared.Dispose();
                failure = ex;
            }
        }

        if (_reconnects > 0 && _reconnects < ReconnectWaits.Count)
        {
            _reconnects++;
            StartConnect(item, _connectPlay, startAt, ReconnectWaits[_reconnects - 1]);
            await PendingOpen;
            return;
        }

        var reason = FriendlyReason(NetworkMedia.Redact(failure?.Message ?? "The address could not be opened.", item.Source));
        CurrentMedia = null;
        SetOpenState(OpenState.Undefined);
        SetPlayState(PlayState.Stopped);
        SetStatus("Cannot play the address");
        SetNetwork(NetworkPhase.Failed, "Cannot play this address. " + reason);
        Error?.Invoke(this, $"{NetworkMedia.Redact(item.Source)}: {reason}");
    }

    /// <summary>FFmpeg gives some network failures as a bare number (the system's): said in words.</summary>
    public static string FriendlyReason(string message)
    {
        // What the server sent is not media at all: in practice, a web page that has a video on it.
        if (message.Contains("Invalid data found when processing input", StringComparison.Ordinal))
            return "This is not audio or video. A link to a web page with a video on it is not a link to the video itself.";
        var match = System.Text.RegularExpressions.Regex.Match(message, @"Error number -(\d+) occurred");
        if (!match.Success)
            return message;
        return match.Groups[1].Value switch
        {
            "138" or "10060" or "110" => "The server did not answer in time.",
            "107" or "10061" or "111" => "The server refused the connection.",
            "10054" or "104" or "108" => "The connection was closed by the server.",
            "10065" or "10051" or "113" or "101" => "The server cannot be reached (check the network).",
            "5" => "The connection failed while reading.",
            var code => $"The connection failed (error {code}).",
        };
    }

    /// <summary>
    /// A network item "ended". A live stream never does, and a file that stops well before its end lost its connection: both
    /// are connected again. True when a new try was started.
    /// </summary>
    private bool TryReconnect()
    {
        if (CurrentMedia is not { } item || !Network.Reconnect || !NetworkMedia.IsNetwork(item.Source) || !_engine.OpensInBackground(item.Source))
            return false;
        var length = _engine.Duration.TotalSeconds;
        var at = _engine.Position.TotalSeconds;
        var live = length <= 0;
        if (!live && at >= length - 5)
            return false;
        if (_reconnects >= ReconnectWaits.Count)
        {
            SetNetwork(NetworkPhase.Failed, "The connection was lost.");
            return false;
        }

        _reconnects++;
        _engine.Close();
        SetOpenState(OpenState.MediaOpening);
        SetPlayState(PlayState.Transitioning);
        StartConnect(item, play: true, startAt: live ? 0 : at, ReconnectWaits[_reconnects - 1]);
        return true;
    }

    private void TickNetwork(TimeSpan elapsed)
    {
        if (_connect is not null || CurrentMedia is not { } item || !NetworkMedia.IsNetwork(item.Source))
            return;
        if (_engine.IsBuffering && _playState == PlayState.Playing)
        {
            SetNetwork(NetworkPhase.Buffering, $"Buffering {_engine.BufferingPercent}%");
            return;
        }

        if (_networkPhase == NetworkPhase.Buffering)
            SetNetwork(NetworkPhase.None);
        if (_reconnects > 0 && _playState == PlayState.Playing)
        {
            // Half a minute of good playing: the next drop starts counting afresh.
            _playedSinceReconnect += elapsed;
            if (_playedSinceReconnect > TimeSpan.FromSeconds(30))
                _reconnects = 0;
        }
    }
}
