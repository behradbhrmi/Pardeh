using Pardeh.Ffmpeg;
using FFmpeg.AutoGen;

namespace Pardeh.Nama;

/// <summary>Decodes the main video stream of a file with FFmpeg into BGRA frames (via swscale).</summary>
/// <remarks>All FFmpeg objects live on the decoder thread.</remarks>
internal sealed unsafe class FfmpegVideoDecoder : TimedVideoDecoder
{
    private readonly string _path;
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private SwsContext* _sws;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private int _stream = -1;
    private AVRational _timeBase;
    private bool _flushing;
    private bool _hasPending;

    private readonly NetworkInput? _network;

    private FfmpegVideoDecoder(string path, Func<TimeSpan> clock, NetworkInput? network) : base(clock, "Pardeh video decoder (FFmpeg)")
    {
        _path = path;
        _network = network;
    }

    protected override void Interrupt() => _network?.Abort.Cancel();

    public override string DecoderName => "FFmpeg";

    /// <summary>Opens the video stream of <paramref name="path"/>, or returns null if it has none (cover art doesn't count).</summary>
    public static FfmpegVideoDecoder? TryOpen(string path, Func<TimeSpan> clock, NetworkInput? network = null)
    {
        var decoder = new FfmpegVideoDecoder(path, clock, network);
        // A server may take much longer than a disk to say what the picture is.
        if (decoder.Start(network?.Options.ConnectTimeout))
            return decoder;
        decoder.Dispose();
        return null;
    }

    protected override void OpenStream()
    {
        AVFormatContext* format = null;
        FfmpegLibrary.Check(FfmpegIo.Open(&format, _path, _network), "Opening the file");
        _format = format;
        FfmpegLibrary.Check(ffmpeg.avformat_find_stream_info(format, null), "Reading stream info");

        AVCodec* decoder = null;
        _stream = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &decoder, 0);
        FfmpegLibrary.Check(_stream, "Finding a video stream");
        var stream = format->streams[_stream];
        if ((stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0)
            throw new InvalidDataException("Only album art, no video.");
        _timeBase = stream->time_base;

        _codec = ffmpeg.avcodec_alloc_context3(decoder);
        FfmpegLibrary.Check(ffmpeg.avcodec_parameters_to_context(_codec, stream->codecpar), "Configuring the decoder");
        _codec->thread_count = 0; // let FFmpeg pick the thread count
        FfmpegLibrary.Check(ffmpeg.avcodec_open2(_codec, decoder, null), "Opening the decoder");

        Width = _codec->width;
        Height = _codec->height;
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();

        if (format->duration > 0)
            Duration = TimeSpan.FromTicks(format->duration * 10);
        else if (stream->duration > 0)
            Duration = TimeSpan.FromSeconds(stream->duration * ffmpeg.av_q2d(_timeBase));
    }

    protected override bool DecodeNext(out TimeSpan time)
    {
        time = TimeSpan.Zero;
        while (true)
        {
            var received = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (received == 0)
            {
                var pts = _frame->best_effort_timestamp;
                time = pts == ffmpeg.AV_NOPTS_VALUE ? TimeSpan.Zero : TimeSpan.FromSeconds(pts * ffmpeg.av_q2d(_timeBase));
                _hasPending = true;
                return true;
            }

            if (received != FfmpegLibrary.EAGAIN || _flushing)
                return false; // end of stream (or an unrecoverable decode error)

            var read = ffmpeg.av_read_frame(_format, _packet);
            if (read < 0)
            {
                _flushing = true;
                ffmpeg.avcodec_send_packet(_codec, null);
                continue;
            }

            if (_packet->stream_index == _stream)
                ffmpeg.avcodec_send_packet(_codec, _packet);
            ffmpeg.av_packet_unref(_packet);
        }
    }

    protected override byte[] ConvertPending()
    {
        // The frame size can change mid-stream; follow it.
        if (_frame->width > 0 && _frame->height > 0)
        {
            Width = _frame->width;
            Height = _frame->height;
        }

        var width = Width;
        var height = Height;

        // FFmpeg allocates the destination (aligned, padded) and configures the scaler from the frames.
        if (_bgra is null)
            _bgra = ffmpeg.av_frame_alloc();
        if (_bgra->width != width || _bgra->height != height || _bgra->data[0] is null)
        {
            ffmpeg.av_frame_unref(_bgra);
            _bgra->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
            _bgra->width = width;
            _bgra->height = height;
            FfmpegLibrary.Check(ffmpeg.av_frame_get_buffer(_bgra, 64), "Allocating a video frame");
        }

        if (_sws is null)
        {
            _sws = ffmpeg.sws_alloc_context();
            // Full-resolution chroma and accurate rounding: the default fast path smears colour edges (cartoons show it).
            // Pictures above DVD size take the cheap fast-bilinear path: the accurate one costs 20 ms or more per 720p frame, too slow for a faster play speed.
            var heavy = (long)width * height > 640L * 480;
            ffmpeg.av_opt_set_int(_sws, "sws_flags", heavy ? SwsFastBilinear :
                SwsBicubic | SwsFullChromaInterpolation | SwsFullChromaInput | SwsAccurateRounding, 0);
            ffmpeg.av_opt_set_int(_sws, "threads", 0, 0);
        }
        FfmpegLibrary.Check(ffmpeg.sws_scale_frame(_sws, _bgra, _frame), "Converting the video frame");

        var rowBytes = width * 4;
        var source = _bgra->data[0];
        var stride = _bgra->linesize[0];
        var pixels = RentFrameBuffer(rowBytes * height);
        fixed (byte* destination = pixels)
        {
            if (stride == rowBytes)
                Buffer.MemoryCopy(source, destination, (long)rowBytes * height, (long)rowBytes * height);
            else
            for (var y = 0; y < height; y++)
                Buffer.MemoryCopy(source + (long)y * stride, destination + (long)y * rowBytes, rowBytes, rowBytes);
        }

        return pixels; // alpha is ignored: video is drawn as an opaque bitmap
    }

    private AVFrame* _bgra;

    // swscale flag values (libswscale/swscale.h).
    private const long SwsFastBilinear = 0x1, SwsBilinear = 0x2, SwsBicubic = 0x4, SwsFullChromaInterpolation = 0x2000, SwsFullChromaInput = 0x4000, SwsAccurateRounding = 0x40000;

    protected override void DropPending()
    {
        if (_hasPending)
            ffmpeg.av_frame_unref(_frame);
        _hasPending = false;
    }

    protected override void SetCatchUp(bool behind)
    {
        // Behind the clock (after a seek): skip decoding frames nothing depends on, and the deblocking filter.
        _codec->skip_frame = behind ? AVDiscard.AVDISCARD_NONREF : AVDiscard.AVDISCARD_DEFAULT;
        _codec->skip_loop_filter = behind ? AVDiscard.AVDISCARD_ALL : AVDiscard.AVDISCARD_DEFAULT;
    }

    protected override void SeekTo(TimeSpan position)
    {
        var timestamp = (long)(Math.Max(0, position.TotalSeconds) / ffmpeg.av_q2d(_timeBase));
        ffmpeg.av_seek_frame(_format, _stream, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ffmpeg.avcodec_flush_buffers(_codec);
        _flushing = false;
    }

    protected override void CloseStream()
    {
        DropPending();
        var sws = _sws;
        ffmpeg.sws_free_context(&sws);
        _sws = null;
        var bgra = _bgra;
        ffmpeg.av_frame_free(&bgra);
        _bgra = null;
        var frame = _frame;
        ffmpeg.av_frame_free(&frame);
        _frame = null;
        var packet = _packet;
        ffmpeg.av_packet_free(&packet);
        _packet = null;
        var codec = _codec;
        ffmpeg.avcodec_free_context(&codec);
        _codec = null;
        var format = _format;
        FfmpegIo.Close(&format);
        _format = null;
        _network?.Abort.Dispose(); // only after the context that reads it is gone
    }
}
