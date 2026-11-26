using Pardeh.Ffmpeg;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Pardeh.Core.Playback;

namespace Pardeh.Nama;

/// <summary>Subtitles inside a media file (Matroska, MP4 ...): lists the text tracks and reads one into cues.</summary>
internal static unsafe class FfmpegSubtitleReader
{
    /// <summary>Text codecs only: picture subtitles (PGS, DVD, DVB) cannot be drawn as text.</summary>
    private static readonly HashSet<AVCodecID> TextCodecs =
    [
        AVCodecID.AV_CODEC_ID_SUBRIP, AVCodecID.AV_CODEC_ID_SRT, AVCodecID.AV_CODEC_ID_ASS, AVCodecID.AV_CODEC_ID_SSA,
        AVCodecID.AV_CODEC_ID_MOV_TEXT, AVCodecID.AV_CODEC_ID_WEBVTT, AVCodecID.AV_CODEC_ID_TEXT, AVCodecID.AV_CODEC_ID_SUBVIEWER,
        AVCodecID.AV_CODEC_ID_SUBVIEWER1, AVCodecID.AV_CODEC_ID_MICRODVD, AVCodecID.AV_CODEC_ID_JACOSUB,
    ];

    private static string? Tag(AVStream* stream, string key)
    {
        var entry = ffmpeg.av_dict_get(stream->metadata, key, null, 0);
        return entry is null ? null : Marshal.PtrToStringUTF8((IntPtr)entry->value);
    }

    public static IReadOnlyList<SubtitleTrack> List(string path)
    {
        var tracks = new List<SubtitleTrack>();
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
                if (stream->codecpar->codec_type != AVMediaType.AVMEDIA_TYPE_SUBTITLE || !TextCodecs.Contains(stream->codecpar->codec_id))
                    continue;
                var language = Tag(stream, "language") is { Length: > 0 } code && code != "und" ? FfmpegMetadata.LanguageName(code) : null;
                var title = Tag(stream, "title");
                var name = string.Join(" – ", new[] { language, title }.Where(s => !string.IsNullOrWhiteSpace(s)));
                tracks.Add(new SubtitleTrack(path, i, name.Length > 0 ? name : $"Subtitle {tracks.Count + 1}"));
            }
        }
        finally
        {
            FfmpegIo.Close(&format);
        }

        return tracks;
    }

    /// <summary>Reads every cue of one subtitle stream (a pass over the file, so run it off the interface thread).</summary>
    public static List<SubtitleCue> Extract(string path, int streamIndex, CancellationToken cancel)
    {
        var cues = new List<SubtitleCue>();
        AVFormatContext* format = null;
        if (FfmpegIo.Open(&format, path) < 0)
            return cues;
        AVCodecContext* codec = null;
        AVPacket* packet = null;
        try
        {
            if (ffmpeg.avformat_find_stream_info(format, null) < 0 || streamIndex < 0 || streamIndex >= (int)format->nb_streams)
                return cues;
            var stream = format->streams[streamIndex];
            var decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (decoder is null)
                return cues;
            codec = ffmpeg.avcodec_alloc_context3(decoder);
            if (ffmpeg.avcodec_parameters_to_context(codec, stream->codecpar) < 0 || ffmpeg.avcodec_open2(codec, decoder, null) < 0)
                return cues;
            codec->pkt_timebase = stream->time_base;

            // Only this stream's packets are wanted: the rest are dropped by the demuxer without being decoded.
            for (var i = 0; i < (int)format->nb_streams; i++)
                format->streams[i]->discard = i == streamIndex ? AVDiscard.AVDISCARD_DEFAULT : AVDiscard.AVDISCARD_ALL;

            var timeBase = ffmpeg.av_q2d(stream->time_base);
            packet = ffmpeg.av_packet_alloc();
            while (!cancel.IsCancellationRequested && ffmpeg.av_read_frame(format, packet) >= 0)
            {
                try
                {
                    if (packet->stream_index != streamIndex)
                        continue;
                    AVSubtitle subtitle;
                    var got = 0;
                    if (ffmpeg.avcodec_decode_subtitle2(codec, &subtitle, &got, packet) < 0 || got == 0)
                        continue;
                    try
                    {
                        var packetStart = packet->pts == ffmpeg.AV_NOPTS_VALUE ? 0 : packet->pts * timeBase;
                        var start = subtitle.pts != ffmpeg.AV_NOPTS_VALUE ? subtitle.pts / (double)ffmpeg.AV_TIME_BASE : packetStart;
                        start += subtitle.start_display_time / 1000.0;
                        var end = subtitle.end_display_time is > 0 and < uint.MaxValue
                            ? start - subtitle.start_display_time / 1000.0 + subtitle.end_display_time / 1000.0
                            : packet->duration > 0 ? start + packet->duration * timeBase : start + 3;
                        if (end <= start)
                            end = start + 3;
                        for (var r = 0; r < (int)subtitle.num_rects; r++)
                        {
                            var rect = subtitle.rects[r];
                            var text = rect->type switch
                            {
                                AVSubtitleType.SUBTITLE_ASS => Marshal.PtrToStringUTF8((IntPtr)rect->ass) is { } ass ? Subtitles.TextOfAssEvent(ass) : null,
                                AVSubtitleType.SUBTITLE_TEXT => Marshal.PtrToStringUTF8((IntPtr)rect->text) is { } plain ? Subtitles.CleanText(plain) : null,
                                _ => null,
                            };
                            if (!string.IsNullOrWhiteSpace(text))
                                cues.Add(new SubtitleCue(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text));
                        }
                    }
                    finally
                    {
                        ffmpeg.avsubtitle_free(&subtitle);
                    }
                }
                finally
                {
                    ffmpeg.av_packet_unref(packet);
                }
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&codec);
            FfmpegIo.Close(&format);
        }

        return cues;
    }
}
