using System.Security.Cryptography;
using FFmpeg.AutoGen;
using Pardeh.Core.Duplicates;
using Pardeh.Ffmpeg;

namespace Pardeh.Nava;

/// <summary>
/// A hash of a file's sound and picture data without its tags: the compressed packets of every stream but cover pictures.
/// Two files with the same digest hold the same recording byte for byte, whatever their tags say; a tag change that alters
/// it has damaged the file.
/// </summary>
public static unsafe class AudioDigest
{
    /// <summary>Whether FFmpeg is there to read files with.</summary>
    public static bool IsAvailable => FfmpegLibrary.IsAvailable;

    /// <summary>Null when FFmpeg is not there or can't read the file.</summary>
    public static string? Compute(string path) => Scan(path)?.Digest;

    /// <summary>Reads the whole file, or only its first packets, and says what its sound is. Null when it can't be read.</summary>
    /// <param name="maxPackets">Stop after this many packets (0: read everything). The digest then covers only those.</param>
    public static AudioScan? Scan(string path, int maxPackets = 0, CancellationToken cancel = default)
    {
        if (!FfmpegLibrary.IsAvailable)
            return null;
        AVFormatContext* format = null;
        if (FfmpegIo.Open(&format, path) < 0)
            return null;
        var packet = ffmpeg.av_packet_alloc();
        var hashes = new SortedDictionary<int, IncrementalHash>();
        try
        {
            if (ffmpeg.avformat_find_stream_info(format, null) < 0)
                return null;
            var sound = -1;
            var video = false;
            for (var i = 0; i < (int)format->nb_streams; i++)
            {
                var stream = format->streams[i];
                var type = stream->codecpar->codec_type;
                if (type is AVMediaType.AVMEDIA_TYPE_AUDIO or AVMediaType.AVMEDIA_TYPE_VIDEO && (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                {
                    hashes[i] = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    video |= type == AVMediaType.AVMEDIA_TYPE_VIDEO;
                }

                if (type == AVMediaType.AVMEDIA_TYPE_AUDIO && sound < 0)
                    sound = i;
            }

            if (hashes.Count == 0)
                return null;
            var codec = string.Empty;
            var lossless = false;
            var rate = 0;
            var declared = 0.0;
            if (sound >= 0)
            {
                var stream = format->streams[sound];
                var id = stream->codecpar->codec_id;
                codec = ffmpeg.avcodec_get_name(id);
                var about = ffmpeg.avcodec_descriptor_get(id);
                lossless = about != null && (about->props & ffmpeg.AV_CODEC_PROP_LOSSLESS) != 0 && (about->props & ffmpeg.AV_CODEC_PROP_LOSSY) == 0;
                rate = stream->codecpar->sample_rate;
                declared = stream->duration > 0 ? stream->duration * ffmpeg.av_q2d(stream->time_base)
                    : format->duration > 0 ? format->duration / (double)ffmpeg.AV_TIME_BASE : 0;
            }

            var packets = 0L;
            var ticks = 0L;
            int result;
            while ((result = ffmpeg.av_read_frame(format, packet)) >= 0)
            {
                if (hashes.TryGetValue(packet->stream_index, out var hash) && packet->size > 0)
                {
                    hash.AppendData(new ReadOnlySpan<byte>(packet->data, packet->size));
                    packets++;
                    if (packet->stream_index == sound && packet->duration > 0)
                        ticks += packet->duration;
                }

                ffmpeg.av_packet_unref(packet);
                if (maxPackets > 0 && packets >= maxPackets)
                {
                    result = ffmpeg.AVERROR_EOF;
                    break;
                }

                if ((packets & 1023) == 0)
                    cancel.ThrowIfCancellationRequested();
            }

            if (result != ffmpeg.AVERROR_EOF || packets == 0)
                return null;
            var seconds = sound >= 0 ? ticks * ffmpeg.av_q2d(format->streams[sound]->time_base) : 0;
            // In stream order, not by number: a cover picture added in front moves the numbers.
            var digest = string.Join('-', hashes.Values.Select(h => Convert.ToHexString(h.GetHashAndReset())));
            return new AudioScan(digest, seconds, declared, codec, lossless, rate) { HasVideo = video };
        }
        finally
        {
            foreach (var hash in hashes.Values)
                hash.Dispose();
            ffmpeg.av_packet_free(&packet);
            FfmpegIo.Close(&format);
        }
    }
}
