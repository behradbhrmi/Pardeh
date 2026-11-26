using Pardeh.Nama;
using Pardeh.Engine;
using System.Text;
using FFmpeg.AutoGen;
using Pardeh.Nava;
using Pardeh.Ffmpeg;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// A real Matroska file with embedded text subtitle tracks, written with the FFmpeg libraries themselves (a tiny raw picture track and
/// two SubRip tracks, English and Persian), then read back by <see cref="FfmpegSubtitleReader"/> and the engine.
/// </summary>
public sealed class MkvSubtitleTests : IDisposable
{
    private const int W = 32, H = 24, Fps = 5, Seconds = 6;
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-mkv-").FullName;
    private string File_ => Path.Combine(_folder, "film.mkv");

    private static readonly (double Start, double Length, string Text)[] English =
        [(0.5, 1.0, "Hello there."), (2.0, 1.5, "<i>Two lines</i>\nof it"), (4.25, 1.0, "Goodbye, all.")];

    private static readonly (double Start, double Length, string Text)[] Persian = [(1.0, 2.0, "سلام دنیا")];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static unsafe AVStream* NewStream(AVFormatContext* format, AVMediaType type, AVCodecID codec, string? language)
    {
        var stream = ffmpeg.avformat_new_stream(format, null);
        stream->codecpar->codec_type = type;
        stream->codecpar->codec_id = codec;
        stream->time_base = new AVRational { num = 1, den = 1000 };
        if (language is not null)
            ffmpeg.av_dict_set(&stream->metadata, "language", language, 0);
        return stream;
    }

    private static unsafe void Write(AVFormatContext* format, AVStream* stream, byte[] data, double start, double length)
    {
        var packet = ffmpeg.av_packet_alloc();
        FfmpegLibrary.Check(ffmpeg.av_new_packet(packet, data.Length), "Allocating a packet");
        for (var i = 0; i < data.Length; i++)
            packet->data[i] = data[i];
        packet->stream_index = stream->index;
        // The muxer chose its own time base when it wrote the header.
        var ticks = 1 / ffmpeg.av_q2d(stream->time_base);
        packet->pts = packet->dts = (long)Math.Round(start * ticks);
        packet->duration = (long)Math.Round(length * ticks);
        packet->flags |= ffmpeg.AV_PKT_FLAG_KEY;
        FfmpegLibrary.Check(ffmpeg.av_interleaved_write_frame(format, packet), "Writing a packet");
        ffmpeg.av_packet_free(&packet);
    }

    private void WriteMkv() => WriteMkv(File_);

    /// <summary>A six-second video without sound, with two subtitle tracks (also used by the network tests).</summary>
    internal static unsafe void WriteMkv(string path)
    {
        AVFormatContext* format = null;
        FfmpegLibrary.Check(ffmpeg.avformat_alloc_output_context2(&format, null, "matroska", path), "Creating the container");
        try
        {
            var video = NewStream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, AVCodecID.AV_CODEC_ID_RAWVIDEO, null);
            video->codecpar->width = W;
            video->codecpar->height = H;
            video->codecpar->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            video->codecpar->codec_tag = ffmpeg.avcodec_pix_fmt_to_codec_tag(AVPixelFormat.AV_PIX_FMT_YUV420P);
            var english = NewStream(format, AVMediaType.AVMEDIA_TYPE_SUBTITLE, AVCodecID.AV_CODEC_ID_SUBRIP, "eng");
            var persian = NewStream(format, AVMediaType.AVMEDIA_TYPE_SUBTITLE, AVCodecID.AV_CODEC_ID_SUBRIP, "per");

            FfmpegLibrary.Check(ffmpeg.avio_open(&format->pb, path, ffmpeg.AVIO_FLAG_WRITE), "Opening the file");
            FfmpegLibrary.Check(ffmpeg.avformat_write_header(format, null), "Writing the header");

            var packets = new List<(double Start, double Length, nint Stream, byte[] Data)>();
            for (var f = 0; f < Fps * Seconds; f++)
                packets.Add((f / (double)Fps, 1.0 / Fps, (nint)video, Enumerable.Repeat((byte)(f * 7), W * H * 3 / 2).ToArray()));
            packets.AddRange(English.Select(c => (c.Start, c.Length, (nint)english, Encoding.UTF8.GetBytes(c.Text))));
            packets.AddRange(Persian.Select(c => (c.Start, c.Length, (nint)persian, Encoding.UTF8.GetBytes(c.Text))));
            foreach (var (start, length, stream, data) in packets.OrderBy(p => p.Start))
                Write(format, (AVStream*)stream, data, start, length);

            FfmpegLibrary.Check(ffmpeg.av_write_trailer(format), "Finishing the file");
        }
        finally
        {
            if (format->pb is not null)
                ffmpeg.avio_closep(&format->pb);
            ffmpeg.avformat_free_context(format);
        }
    }

    [Fact]
    public void Embedded_tracks_are_listed_and_read_with_their_times()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, $"FFmpeg not available: {FfmpegLibrary.LoadError}");
        WriteMkv();

        var tracks = FfmpegSubtitleReader.List(File_);
        Assert.Equal(["English", "Persian"], tracks.Select(t => t.Label));

        var cues = FfmpegSubtitleReader.Extract(File_, tracks[0].Stream, TestContext.Current.CancellationToken);
        Assert.Equal(["Hello there.", "Two lines\nof it", "Goodbye, all."], cues.Select(c => c.Text.Replace("\r", string.Empty)));
        for (var i = 0; i < cues.Count; i++)
        {
            Assert.InRange(cues[i].Start.TotalSeconds, English[i].Start - 0.01, English[i].Start + 0.01);
            Assert.InRange(cues[i].End.TotalSeconds, English[i].Start + English[i].Length - 0.01, English[i].Start + English[i].Length + 0.01);
        }

        var persian = FfmpegSubtitleReader.Extract(File_, tracks[1].Stream, TestContext.Current.CancellationToken);
        Assert.Equal("سلام دنیا", Assert.Single(persian).Text);
    }

    [Fact]
    public async Task The_engine_offers_the_tracks_of_an_open_video()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, $"FFmpeg not available: {FfmpegLibrary.LoadError}");
        WriteMkv();
        using var engine = new MediaEngine();
        engine.Open(File_); // no sound in the file: the picture runs on a timer, no audio device is used
        Assert.Equal((W, H), engine.VideoSize);
        for (var i = 0; i < 100 && engine.SubtitleTracks.Count == 0; i++)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(2, engine.SubtitleTracks.Count);
        Assert.Equal(3, engine.ReadSubtitleTrack(engine.SubtitleTracks[0], TestContext.Current.CancellationToken)!.Count);
    }
}
