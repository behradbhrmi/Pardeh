using Pardeh.Core.Playback;
using Pardeh.Ffmpeg;
using Pardeh.Nama;
using Pardeh.Nava;

namespace Pardeh.Engine;

/// <summary>
/// The app's playback engine: Nava plays the sound, Nama shows the picture, and this puts them together. It decides which
/// of the two an item needs, times the picture to the sound being heard (or to Nama's own timer for a video without
/// sound), and passes the player's controls to whichever of them they concern.
/// </summary>
public sealed partial class MediaEngine : IPlaybackEngine
{
    private readonly NavaPlayer _sound = new();
    private readonly NamaPlayer _picture;
    private double _rate = 1;

    public MediaEngine()
    {
        _picture = new NamaPlayer(() => _sound.HeardPosition, () => _sound.IsPlaying);
        _picture.Ended += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        _sound.Ended += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        _sound.Failed += (_, e) => PlaybackFailed?.Invoke(this, e);
        _sound.Seeked += _picture.Resync;
        _sound.AdvancedToNext += (_, source) => AdvancedToNext?.Invoke(this, source);
    }

    public event EventHandler? PlaybackEnded;

    /// <summary>Raised (on the audio thread) when audio output stops because of an error.</summary>
    public event EventHandler<Exception>? PlaybackFailed;

    public event EventHandler<string>? AdvancedToNext;

    /// <summary>Play speed, 0.25 – 4 (pitch kept). Video follows because it is timed off the audio position.</summary>
    public double Rate
    {
        get => _rate;
        set
        {
            _rate = Math.Clamp(value, 0.25, 4);
            _sound.Rate = _rate;
            _picture.Rate = _rate;
        }
    }

    public bool IsOpen => _sound.IsOpen || _picture.KeepsTime;

    public TimeSpan Duration => _sound.IsOpen ? _sound.Duration : _picture.Duration;

    public (int Width, int Height) VideoSize => _picture.Size;

    public VideoFrame? CurrentVideoFrame => _picture.CurrentFrame;

    public TimeSpan Position
    {
        get => _picture.KeepsTime ? _picture.Position : _sound.Position;
        set
        {
            if (_picture.KeepsTime)
                _picture.Position = value;
            else
                _sound.Position = value; // the picture follows when the seek has been carried out
        }
    }

    public EqualizerSettings Equalizer => _sound.Equalizer;

    public float Volume
    {
        get => _sound.Volume;
        set => _sound.Volume = value;
    }

    public float Balance
    {
        get => _sound.Balance;
        set => _sound.Balance = value;
    }

    public bool Muted
    {
        get => _sound.Muted;
        set => _sound.Muted = value;
    }

    public void SetLevelFactor(float factor) => _sound.SetLevelFactor(factor);

    public void Open(string source)
    {
        Close();

        var sound = NavaPlayer.OpenSource(source, out var soundError);

        // Internet radio: audio only (a video probe would open a second connection to the stream).
        var isStream = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!isStream && !NavaPlayer.IsAudioFile(source))
            _picture.Show(_picture.OpenFile(source));

        if (_picture.HasPicture && FfmpegLibrary.IsAvailable && !isStream)
            _picture.FindSubtitleTracks(source);

        if (sound is null)
        {
            if (!_picture.HasPicture)
                throw soundError ?? new InvalidDataException("No playable audio or video stream.");
            _picture.StartWithoutSound();
            return;
        }

        try
        {
            _sound.Start(sound);
        }
        catch
        {
            sound.Dispose();
            _picture.Close();
            throw;
        }

        if (!isStream)
            _sound.ListTracks(source);
    }

    // ---- Gapless playback and crossfading: sound only -------------------------------------------------------------

    public void PrepareNext(string source, float levelFactor)
    {
        if (!_picture.HasPicture)
            _sound.PrepareNext(source, levelFactor);
    }

    public void DropNext() => _sound.DropNext();

    /// <summary>
    /// Crossfading: the playing track keeps going while it fades out over <paramref name="seconds"/>, and
    /// <paramref name="source"/> opens in its own output, fading in. Falls back to a plain open for video.
    /// </summary>
    public void OpenCrossfade(string source, double seconds)
    {
        if (!_sound.CanCrossfade || _picture.HasPicture || seconds <= 0)
        {
            Open(source);
            return;
        }

        _sound.FadeOut(seconds);
        Open(source);
        _sound.FadeIn(seconds);
    }

    // ---- Audio tracks, chapters and subtitles ---------------------------------------------------------------------

    public IReadOnlyList<AudioTrack> AudioTracks => _sound.Tracks;

    public IReadOnlyList<Chapter> Chapters => _sound.Chapters;

    public int CurrentAudioTrack => _sound.CurrentTrack;

    /// <summary>Plays another audio track (a dub, the original language …) from the same position.</summary>
    public void SelectAudioTrack(int index) => _sound.SelectTrack(index);

    /// <summary>Adds a separate audio file (e.g. a dub downloaded on its own) and switches to it.</summary>
    public bool AddAudioFile(string path) => _sound.AddFile(path);

    public IReadOnlyList<SubtitleTrack> SubtitleTracks => _picture.SubtitleTracks;

    public IReadOnlyList<SubtitleCue>? ReadSubtitleTrack(SubtitleTrack track, CancellationToken cancel) => NamaPlayer.ReadSubtitleTrack(track, cancel);

    /// <summary>"FFmpeg" / "Media Foundation" for the open audio and video streams (diagnostics).</summary>
    public string DecoderInfo =>
        $"audio: {_sound.DecoderName ?? "none"}, video: {_picture.DecoderName ?? "none"}, ffmpeg: {FfmpegLibrary.Version ?? FfmpegLibrary.LoadError ?? "not loaded"}";

    // ---- Transport ------------------------------------------------------------------------------------------------

    public void Close()
    {
        _sound.CloseOutput(); // silent first
        _picture.Close();
        _sound.Close();
        _sound.ClearTracks();
    }

    public void Play()
    {
        _picture.Play();
        _sound.Play();
    }

    public void Pause()
    {
        _picture.Pause();
        _sound.Pause();
    }

    public void Stop()
    {
        _picture.Pause();
        _sound.Stop();
    }

    public bool GetWaveform(Span<float> samples) => _sound.GetWaveform(samples);

    public bool GetSpectrum(Span<float> bins) => _sound.GetSpectrum(bins);

    public void Dispose()
    {
        Close();
        _sound.Dispose();
    }
}
