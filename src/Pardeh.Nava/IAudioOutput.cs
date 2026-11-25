using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>
/// Where the mixed audio ends up: WASAPI on Windows, AudioTrack on Android. The engine only talks to this.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Connects the sample source (interleaved float, any rate/channel count the pipeline produces).</summary>
    void Init(ISampleProvider source);

    void Play();

    void Pause();

    /// <summary>Stops playback; <see cref="PlaybackStopped"/> follows (also when the source ran out).</summary>
    void Stop();

    PlaybackState PlaybackState { get; }

    /// <summary>Raised (not necessarily on the caller's thread) once the output has stopped; the sender is this output.</summary>
    event EventHandler<StoppedEventArgs>? PlaybackStopped;
}
