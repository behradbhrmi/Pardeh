using Pardeh.Ffmpeg;
using FFmpeg.AutoGen;
using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>Decodes the best audio stream of a file with FFmpeg into interleaved float stereo.</summary>
/// <remarks>Not thread-safe: the engine serialises Read and seeking.</remarks>
internal sealed unsafe class FfmpegAudioSource : IAudioSource
{
    private const int MaxOutputRate = 96000;

    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private SwrContext* _swr;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private readonly int _stream;
    private readonly AVRational _timeBase;
    private readonly int _rate;

    private float[] _pending = new float[8192];
    private int _pendingStart;
    private int _pendingCount;
    private long _framePosition;     // output position (sample frames) of the next sample Read returns
    private double? _discardUntil;   // after a seek: drop decoded audio before this time (seconds)
    private bool _flushing;
    private bool _disposed;

    /// <summary>The file's stream number of the audio being played.</summary>
    public int StreamIndex => _stream;

    /// <summary>The chapters of a file (AVFormatContext.chapters), in time order; empty if it has none.</summary>
    public static IReadOnlyList<Pardeh.Core.Playback.Chapter> ListChapters(string path)
    {
        var chapters = new List<Pardeh.Core.Playback.Chapter>();
        AVFormatContext* format = null;
        if (FfmpegIo.Open(&format, path) < 0)
            return chapters;
        try
        {
            for (var i = 0; i < (int)format->nb_chapters; i++)
            {
                var chapter = format->chapters[i];
                var scale = ffmpeg.av_q2d(chapter->time_base);
                var entry = ffmpeg.av_dict_get(chapter->metadata, "title", null, 0);
                var title = entry is null ? null : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)entry->value);
                chapters.Add(new Pardeh.Core.Playback.Chapter(
                    string.IsNullOrWhiteSpace(title) ? $"Chapter {i + 1}" : title.Trim(),
                    TimeSpan.FromSeconds(chapter->start * scale),
                    TimeSpan.FromSeconds(chapter->end * scale)));
            }
        }
        finally
        {
            FfmpegIo.Close(&format);
        }

        return [.. chapters.OrderBy(c => c.Start)];
    }

    /// <summary>The audio tracks of a file: stream number and a label ("Persian – Dubbed (AAC, 2 ch)").</summary>
    public static IReadOnlyList<(int Stream, string Label)> ListTracks(string path)
    {
        var tracks = new List<(int, string)>();
        AVFormatContext* format = null;
        if (FfmpegIo.Open(&format, path) < 0)
            return tracks;
        try
        {
            if (ffmpeg.avformat_find_stream_info(format, null) < 0)
                return tracks;
            for (var i = 0; i < (int)format->nb_streams; i++)
            {
                var stream = format->streams[i];
                if (stream->codecpar->codec_type != AVMediaType.AVMEDIA_TYPE_AUDIO)
                    continue;
                string? Tag(string key)
                {
                    var entry = ffmpeg.av_dict_get(stream->metadata, key, null, 0);
                    return entry is null ? null : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)entry->value);
                }

                var language = Tag("language") is { Length: > 0 } code && code != "und" ? FfmpegMetadata.LanguageName(code) : null;
                var title = Tag("title");
                var codec = ffmpeg.avcodec_get_name(stream->codecpar->codec_id)?.ToUpperInvariant();
                var details = $"{codec}, {stream->codecpar->ch_layout.nb_channels} ch";
                var name = string.Join(" – ", new[] { language, title }.Where(s => !string.IsNullOrWhiteSpace(s)));
                tracks.Add((i, $"{(name.Length > 0 ? name : $"Track {tracks.Count + 1}")} ({details})"));
            }
        }
        finally
        {
            FfmpegIo.Close(&format);
        }

        return tracks;
    }

    private readonly NetworkAbort? _abort;

    /// <summary>True when the input also has a picture (cover art does not count).</summary>
    public bool HasVideo { get; private set; }

    /// <param name="streamIndex">The audio stream to play (a dubbed language …); -1 picks the best one.</param>
    /// <param name="network">For a network address; this object then owns its abort switch.</param>
    public FfmpegAudioSource(string path, int streamIndex = -1, NetworkInput? network = null)
    {
        _abort = network?.Abort;
        try
        {
            AVFormatContext* format = null;
            FfmpegLibrary.Check(FfmpegIo.Open(&format, path, network), network is null ? "Opening the file" : "Connecting");
            _format = format;
            FfmpegLibrary.Check(ffmpeg.avformat_find_stream_info(format, null), "Reading stream info");

            AVCodec* decoder = null;
            _stream = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, streamIndex, -1, &decoder, 0);
            for (var i = 0; i < (int)format->nb_streams; i++)
            {
                var candidate = format->streams[i];
                if (candidate->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && (candidate->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                    HasVideo = true;
            }

            if (_stream < 0)
                throw new NoAudioStreamException();
            var stream = format->streams[_stream];
            _timeBase = stream->time_base;

            _codec = ffmpeg.avcodec_alloc_context3(decoder);
            FfmpegLibrary.Check(ffmpeg.avcodec_parameters_to_context(_codec, stream->codecpar), "Configuring the decoder");
            FfmpegLibrary.Check(ffmpeg.avcodec_open2(_codec, decoder, null), "Opening the decoder");

            _rate = _codec->sample_rate;
            if (_rate <= 0)
                throw new InvalidDataException("The audio stream has no sample rate.");
            if (_rate > MaxOutputRate)
                _rate = 48000;

            if (_codec->ch_layout.nb_channels <= 0)
                ffmpeg.av_channel_layout_default(&_codec->ch_layout, 2);

            AVChannelLayout stereo;
            ffmpeg.av_channel_layout_default(&stereo, 2);
            SwrContext* swr = null;
            FfmpegLibrary.Check(ffmpeg.swr_alloc_set_opts2(&swr, &stereo, AVSampleFormat.AV_SAMPLE_FMT_FLT, _rate,
                &_codec->ch_layout, _codec->sample_fmt, _codec->sample_rate, 0, null), "Configuring the resampler");
            _swr = swr;
            FfmpegLibrary.Check(ffmpeg.swr_init(swr), "Starting the resampler");

            _packet = ffmpeg.av_packet_alloc();
            _frame = ffmpeg.av_frame_alloc();
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_rate, 2);

            if (format->duration > 0)
                TotalTime = TimeSpan.FromTicks(format->duration * 10); // AV_TIME_BASE is microseconds
            else if (stream->duration > 0)
                TotalTime = TimeSpan.FromSeconds(stream->duration * ffmpeg.av_q2d(_timeBase));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    public TimeSpan TotalTime { get; }

    public string DecoderName => "FFmpeg";

    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds((double)_framePosition / _rate);
        set => Seek(value);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var written = 0;
        while (written < count && !_disposed)
        {
            if (_pendingCount > 0)
            {
                var n = Math.Min(count - written, _pendingCount);
                // Element copy: NAudio may pass a byte[] aliased as float[], which Array.Copy rejects.
                for (var i = 0; i < n; i++)
                    buffer[offset + written + i] = _pending[_pendingStart + i];
                _pendingStart += n;
                _pendingCount -= n;
                written += n;
                _framePosition += n / 2;
                continue;
            }

            if (!DecodeMore())
                break;
        }

        return written;
    }

    /// <summary>Decodes until some output is pending. False at end of stream.</summary>
    private bool DecodeMore()
    {
        while (true)
        {
            var received = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (received == 0)
            {
                Convert();
                ffmpeg.av_frame_unref(_frame);
                if (_pendingCount > 0)
                    return true;
                continue;
            }

            if (received != FfmpegLibrary.EAGAIN)
                return false; // AVERROR_EOF or a decode error

            if (_flushing)
                return false;

            var read = ffmpeg.av_read_frame(_format, _packet);
            if (read < 0)
            {
                _flushing = true;
                ffmpeg.avcodec_send_packet(_codec, null); // drain the decoder
                continue;
            }

            if (_packet->stream_index == _stream)
                ffmpeg.avcodec_send_packet(_codec, _packet);
            ffmpeg.av_packet_unref(_packet);
        }
    }

    private void Convert()
    {
        var capacity = ffmpeg.swr_get_out_samples(_swr, _frame->nb_samples);
        if (capacity <= 0)
            return;
        if (_pending.Length < capacity * 2)
            _pending = new float[capacity * 2];

        int converted;
        fixed (float* destination = _pending)
        {
            var output = (byte*)destination;
            converted = ffmpeg.swr_convert(_swr, &output, capacity, _frame->extended_data, _frame->nb_samples);
        }

        if (converted <= 0)
            return;
        _pendingStart = 0;
        _pendingCount = converted * 2;

        if (_discardUntil is not { } target)
            return;

        // Precise seeking: the demuxer lands on a packet at or before the target; drop the overshoot.
        var pts = _frame->best_effort_timestamp;
        if (pts == ffmpeg.AV_NOPTS_VALUE)
        {
            _discardUntil = null;
            return;
        }

        var frameStart = (long)Math.Round(pts * ffmpeg.av_q2d(_timeBase) * _rate);
        var targetFrame = (long)Math.Round(target * _rate);
        var drop = targetFrame - frameStart;
        if (drop >= converted)
        {
            _pendingCount = 0;
            return;
        }

        if (drop > 0)
        {
            _pendingStart = (int)drop * 2;
            _pendingCount -= (int)drop * 2;
        }

        _discardUntil = null;
        _framePosition = Math.Max(0, frameStart + Math.Max(0, drop));
    }

    private void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;
        var timestamp = (long)(position.TotalSeconds / ffmpeg.av_q2d(_timeBase));
        ffmpeg.av_seek_frame(_format, _stream, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ffmpeg.avcodec_flush_buffers(_codec);
        ffmpeg.swr_init(_swr); // drop buffered resampler state
        _pendingCount = 0;
        _flushing = false;
        _discardUntil = position.TotalSeconds;
        _framePosition = (long)(position.TotalSeconds * _rate);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var frame = _frame;
        ffmpeg.av_frame_free(&frame);
        _frame = null;
        var packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;
        var swr = _swr;
        ffmpeg.swr_free(&swr);
        _swr = null;
        var codec = _codec;
        ffmpeg.avcodec_free_context(&codec);
        _codec = null;
        var format = _format;
        FfmpegIo.Close(&format);
        _format = null;
        _abort?.Dispose(); // only after the context that reads it is gone
    }
}

internal sealed class NoAudioStreamException() : IOException("There is no audio stream.");
