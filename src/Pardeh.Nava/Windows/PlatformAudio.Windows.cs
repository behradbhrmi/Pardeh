#pragma warning disable CA1416 // Windows-only file: only compiled into the net11.0 (desktop) build.
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pardeh.Nava;

/// <summary>Windows choices: WASAPI output, Media Foundation as the decoder fallback.</summary>
internal static partial class PlatformAudio
{
    public static IAudioOutput CreateOutput() => new WasapiAudioOutput();

    public static IAudioSource? OpenFallbackAudio(string source) => new MediaFoundationAudioSource(source);

}

/// <summary>Shared-mode, event-driven WASAPI.</summary>
internal sealed class WasapiAudioOutput : IAudioOutput
{
    private readonly WasapiOut _out = new(AudioClientShareMode.Shared, useEventSync: true, latency: 100);

    public WasapiAudioOutput() => _out.PlaybackStopped += (_, e) => PlaybackStopped?.Invoke(this, e);

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public PlaybackState PlaybackState => _out.PlaybackState;

    public void Init(ISampleProvider source) => _out.Init(source);

    public void Play() => _out.Play();

    public void Pause() => _out.Pause();

    public void Stop() => _out.Stop();

    public void Dispose() => _out.Dispose();
}

/// <summary>Windows Media Foundation decoding (the fallback when FFmpeg isn't installed).</summary>
internal sealed class MediaFoundationAudioSource : IAudioSource
{
    private readonly MediaFoundationReader _reader;
    private readonly SampleChannel _samples;

    public MediaFoundationAudioSource(string path)
    {
        _reader = new MediaFoundationReader(path);
        _samples = new SampleChannel(_reader, forceStereo: true);
    }

    public WaveFormat WaveFormat => _samples.WaveFormat;

    public TimeSpan TotalTime => _reader.TotalTime;

    public TimeSpan CurrentTime { get => _reader.CurrentTime; set => _reader.CurrentTime = value; }

    public string DecoderName => "Media Foundation";

    public int Read(float[] buffer, int offset, int count) => _samples.Read(buffer, offset, count);

    public void Dispose() => _reader.Dispose();
}
