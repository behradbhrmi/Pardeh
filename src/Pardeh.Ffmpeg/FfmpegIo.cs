using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Pardeh.Core.Library;

namespace Pardeh.Ffmpeg;

/// <summary>
/// Opens and closes FFmpeg inputs. A plain path or URL goes straight to avformat_open_input; an Android content URI is read
/// through a custom AVIOContext over the seekable stream the head provides (<see cref="MediaSources.ContentOpener"/>), since
/// the system does not let the process re-open it by path.
/// </summary>
internal static unsafe partial class FfmpegIo
{
    private const int BufferSize = 64 * 1024;
    private static readonly Dictionary<IntPtr, Holder> Live = [];

    private sealed class Holder(Stream stream) : IDisposable
    {
        public Stream Stream { get; } = stream;

        public GCHandle Handle { get; set; }

        public AVIOContext* Context { get; set; }

        public void Dispose()
        {
            if (Context is not null)
            {
                ffmpeg.av_freep(&Context->buffer);
                var context = Context;
                ffmpeg.avio_context_free(&context);
                Context = null;
            }

            if (Handle.IsAllocated)
                Handle.Free();
            Stream.Dispose();
        }
    }

    public static int Open(AVFormatContext** format, string source)
    {
        if (!MediaSources.IsContent(source) || MediaSources.ContentOpener?.Invoke(source) is not { } stream)
            return ffmpeg.avformat_open_input(format, source, null, null);

        var holder = new Holder(stream);
        holder.Handle = GCHandle.Alloc(holder);
        var buffer = (byte*)ffmpeg.av_malloc(BufferSize);
        holder.Context = ffmpeg.avio_alloc_context(buffer, BufferSize, 0, (void*)GCHandle.ToIntPtr(holder.Handle),
            new avio_alloc_context_read_packet_func { Pointer = (nint)(delegate* unmanaged[Cdecl]<void*, byte*, int, int>)&ReadPacket },
            default,
            new avio_alloc_context_seek_func { Pointer = (nint)(delegate* unmanaged[Cdecl]<void*, long, int, long>)&Seek });
        var context = ffmpeg.avformat_alloc_context();
        context->pb = holder.Context;
        context->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;
        *format = context;
        var result = ffmpeg.avformat_open_input(format, null, null, null);
        if (result < 0)
        {
            holder.Dispose(); // the failed open freed the format context itself
            return result;
        }

        lock (Live)
            Live[(IntPtr)holder.Context] = holder;
        return result;
    }

    public static void Close(AVFormatContext** format)
    {
        var pb = *format is null ? IntPtr.Zero : (IntPtr)(*format)->pb;
        ffmpeg.avformat_close_input(format);
        if (pb == IntPtr.Zero)
            return;
        Holder? holder;
        lock (Live)
            Live.Remove(pb, out holder);
        holder?.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ReadPacket(void* opaque, byte* buffer, int size)
    {
        try
        {
            var holder = (Holder)GCHandle.FromIntPtr((IntPtr)opaque).Target!;
            var read = holder.Stream.Read(new Span<byte>(buffer, size));
            return read > 0 ? read : ffmpeg.AVERROR_EOF;
        }
        catch (Exception)
        {
            return ffmpeg.AVERROR_EOF;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Seek(void* opaque, long offset, int whence)
    {
        try
        {
            var holder = (Holder)GCHandle.FromIntPtr((IntPtr)opaque).Target!;
            if ((whence & ffmpeg.AVSEEK_SIZE) != 0)
                return holder.Stream.Length;
            var origin = (whence & 3) switch { 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin };
            return holder.Stream.Seek(offset, origin);
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
