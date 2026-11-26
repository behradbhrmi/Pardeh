using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Pardeh.Core.Playback;

namespace Pardeh.Ffmpeg;

/// <summary>How one network input is opened: the user's options and the switch that makes FFmpeg give up.</summary>
internal sealed record NetworkInput(NetworkOptions Options, NetworkAbort Abort);

/// <summary>
/// The flag FFmpeg's interrupt callback reads during blocking network calls: set by <see cref="Cancel"/>, or by the deadline
/// while connecting. It must live as long as the format context it was given to; whoever closes that context disposes it.
/// </summary>
internal sealed unsafe class NetworkAbort : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public int Cancelled;
        public long Deadline; // Environment.TickCount64; 0: none
    }

    private readonly Lock _gate = new();
    private State* _state = (State*)NativeMemory.AllocZeroed((nuint)sizeof(State));

    public void Cancel()
    {
        lock (_gate)
        {
            if (_state is not null)
                Volatile.Write(ref _state->Cancelled, 1);
        }
    }

    /// <summary>The calls from now on must finish within <paramref name="time"/> (null: no limit of ours).</summary>
    public void Limit(TimeSpan? time)
    {
        lock (_gate)
        {
            if (_state is not null)
                Volatile.Write(ref _state->Deadline, time is { } limit ? Environment.TickCount64 + (long)limit.TotalMilliseconds : 0);
        }
    }

    public AVIOInterruptCB Callback => new()
    {
        callback = new AVIOInterruptCB_callback_func { Pointer = (nint)(delegate* unmanaged[Cdecl]<void*, int>)&Interrupted },
        opaque = _state,
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Interrupted(void* opaque)
    {
        var state = (State*)opaque;
        if (Volatile.Read(ref state->Cancelled) != 0)
            return 1;
        var deadline = Volatile.Read(ref state->Deadline);
        return deadline != 0 && Environment.TickCount64 > deadline ? 1 : 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state is not null)
                NativeMemory.Free(_state);
            _state = null;
        }
    }
}

internal static unsafe partial class FfmpegIo
{
    /// <summary>False for an FFmpeg built without TLS (the Android libraries): https then goes through <see cref="HttpsRelay"/>.</summary>
    /// <remarks>PARDEH_HTTPS_RELAY=1 is a development aid: use the relay with an FFmpeg that has TLS too.</remarks>
    private static readonly Lazy<bool> HasHttps = new(() => Environment.GetEnvironmentVariable("PARDEH_HTTPS_RELAY") != "1"
        && ffmpeg.avio_find_protocol_name("https://host/") is not null);

    /// <summary>Opens a network input with the options for its protocol; blocking calls give up when the input's abort says so.</summary>
    public static int Open(AVFormatContext** format, string source, NetworkInput? network)
    {
        if (network is null)
            return Open(format, source);
        var scheme = source[..Math.Max(0, source.IndexOf("://", StringComparison.Ordinal))].ToLowerInvariant();
        if (scheme == "https" && !HasHttps.Value)
        {
            source = HttpsRelay.Shared.Local(source);
            scheme = "http";
        }

        var context = ffmpeg.avformat_alloc_context();
        context->interrupt_callback = network.Abort.Callback;
        *format = context;

        AVDictionary* options = null;
        if (scheme is "http" or "https")
        {
            ffmpeg.av_dict_set(&options, "user_agent", network.Options.UserAgent, 0);
            ffmpeg.av_dict_set(&options, "rw_timeout", "15000000", 0); // microseconds
            if (network.Options.Reconnect)
            {
                // A dropped connection is taken up again inside FFmpeg, from the same byte where the server allows it.
                ffmpeg.av_dict_set(&options, "reconnect", "1", 0);
                ffmpeg.av_dict_set(&options, "reconnect_streamed", "1", 0);
                ffmpeg.av_dict_set(&options, "reconnect_delay_max", "5", 0);
            }
        }
        else if (scheme is "rtsp" or "rtsps")
        {
            ffmpeg.av_dict_set(&options, "rtsp_transport", "tcp", 0);
            ffmpeg.av_dict_set(&options, "timeout", "15000000", 0);
        }

        try
        {
            return ffmpeg.avformat_open_input(format, source, null, &options); // frees the context and clears *format when it fails
        }
        finally
        {
            ffmpeg.av_dict_free(&options);
        }
    }
}
