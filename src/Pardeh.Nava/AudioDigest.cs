using System.Security.Cryptography;
using FFmpeg.AutoGen;
using Pardeh.Ffmpeg;

namespace Pardeh.Nava;

/// <summary>
/// A hash of a file's sound and picture data without its tags: the compressed packets of every stream but cover pictures.
/// Two files with the same digest hold the same recording byte for byte, whatever their tags say; a tag change that alters
/// it has damaged the file.
/// </summary>
public static unsafe class AudioDigest
{
    /// <summary>Null when FFmpeg is not there or can't read the file.</summary>
    public static string? Compute(string path)
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
            for (var i = 0; i < (int)format->nb_streams; i++)
            {
                var stream = format->streams[i];
                var type = stream->codecpar->codec_type;
                if (type is AVMediaType.AVMEDIA_TYPE_AUDIO or AVMediaType.AVMEDIA_TYPE_VIDEO && (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                    hashes[i] = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            }

            if (hashes.Count == 0)
                return null;
            var packets = 0L;
            int result;
            while ((result = ffmpeg.av_read_frame(format, packet)) >= 0)
            {
                if (hashes.TryGetValue(packet->stream_index, out var hash) && packet->size > 0)
                {
                    hash.AppendData(new ReadOnlySpan<byte>(packet->data, packet->size));
                    packets++;
                }

                ffmpeg.av_packet_unref(packet);
            }

            if (result != ffmpeg.AVERROR_EOF || packets == 0)
                return null;
            // In stream order, not by number: a cover picture added in front moves the numbers.
            return string.Join('-', hashes.Values.Select(h => Convert.ToHexString(h.GetHashAndReset())));
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
