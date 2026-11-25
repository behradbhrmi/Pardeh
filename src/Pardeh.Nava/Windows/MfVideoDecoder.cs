using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.MediaFoundation;

namespace Pardeh.Nava;

/// <summary>Video decoding with Windows Media Foundation (the fallback when FFmpeg isn't installed).</summary>
/// <remarks>Media Foundation objects live only on the decoder thread.</remarks>
[SupportedOSPlatform("windows")]
internal sealed class MfVideoDecoder : TimedVideoDecoder
{
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
    private const int AllStreams = unchecked((int)0xFFFFFFFE);
    private const int MediaSource = unchecked((int)0xFFFFFFFF);

    private static readonly Guid EnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private static readonly Guid FrameSizeKey = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid DefaultStrideKey = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid DurationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");

    private readonly string _path;
    private IMFSourceReader? _reader;
    private IMFSample? _pending;
    private int _stride;

    private MfVideoDecoder(string path, Func<TimeSpan> clock) : base(clock, "Pardeh video decoder (Media Foundation)")
    {
        _path = path;
    }

    public override string DecoderName => "Media Foundation";

    /// <summary>Opens the video stream of <paramref name="path"/>, or returns null if it has none.</summary>
    public static MfVideoDecoder? TryOpen(string path, Func<TimeSpan> clock)
    {
        var decoder = new MfVideoDecoder(path, clock);
        if (decoder.Start())
            return decoder;
        decoder.Dispose();
        return null;
    }

    protected override void OpenStream()
    {
        MediaFoundationApi.Startup();
        var attributes = MediaFoundationApi.CreateAttributes(1);
        attributes.SetUINT32(EnableVideoProcessing, 1);
        MediaFoundationInterop.MFCreateSourceReaderFromURL(_path, attributes, out var reader);
        Marshal.ReleaseComObject(attributes);
        _reader = reader;

        reader.SetStreamSelection(AllStreams, false);
        reader.SetStreamSelection(FirstVideoStream, true); // throws if there is no video stream

        var type = MediaFoundationApi.CreateMediaType();
        type.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE, MediaTypes.MFMediaType_Video);
        type.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE, Rgb32);
        reader.SetCurrentMediaType(FirstVideoStream, IntPtr.Zero, type);
        Marshal.ReleaseComObject(type);

        ReadFormat(reader);
        Duration = ReadDuration(reader);
    }

    protected override bool DecodeNext(out TimeSpan time)
    {
        time = TimeSpan.Zero;
        var reader = _reader!;
        while (true)
        {
            reader.ReadSample(FirstVideoStream, 0, out _, out var flags, out var timestamp, out var sample);
            if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
                ReadFormat(reader);
            if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                if (sample is not null)
                    Marshal.ReleaseComObject(sample);
                return false;
            }

            if (sample is null)
                continue;

            _pending = sample;
            time = TimeSpan.FromTicks((long)timestamp);
            return true;
        }
    }

    protected override void DropPending()
    {
        if (_pending is not null)
            Marshal.ReleaseComObject(_pending);
        _pending = null;
    }

    protected override void SeekTo(TimeSpan position)
    {
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            for (var i = 0; i < 24; i++)
                Marshal.WriteByte(variant, i, 0);
            Marshal.WriteInt16(variant, 0, 20); // VT_I8
            Marshal.WriteInt64(variant, 8, Math.Max(0, position.Ticks));
            _reader!.SetCurrentPosition(Guid.Empty, variant);
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }
    }

    protected override void CloseStream()
    {
        DropPending();
        if (_reader is not null)
            Marshal.ReleaseComObject(_reader);
        _reader = null;
    }

    protected override byte[] ConvertPending()
    {
        var width = Width;
        var height = Height;
        var rowBytes = width * 4;
        var pixels = new byte[rowBytes * height];

        _pending!.GetBufferByIndex(0, out var buffer);
        try
        {
            if (buffer is IMF2DBuffer buffer2D)
            {
                // scanline0 is the top row in display order; pitch is negative for bottom-up images.
                buffer2D.Lock2D(out var scanline0, out var pitch);
                try
                {
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(scanline0 + y * pitch, pixels, y * rowBytes, rowBytes);
                }
                finally
                {
                    buffer2D.Unlock2D();
                }
            }
            else
            {
                buffer.Lock(out var data, out _, out var length);
                try
                {
                    var stride = _stride != 0 ? _stride : height > 0 ? length / height : rowBytes;
                    if (Math.Abs(stride) < rowBytes)
                        stride = rowBytes;
                    for (var y = 0; y < height; y++)
                    {
                        var sourceRow = stride > 0 ? y * stride : (height - 1 - y) * -stride;
                        if (sourceRow + rowBytes > length)
                            break;
                        Marshal.Copy(data + sourceRow, pixels, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    buffer.Unlock();
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }

        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255; // RGB32's fourth byte is undefined
        return pixels;
    }

    private void ReadFormat(IMFSourceReader reader)
    {
        reader.GetCurrentMediaType(FirstVideoStream, out var current);
        try
        {
            current.GetUINT64(FrameSizeKey, out var packedSize);
            Width = (int)(packedSize >> 32);
            Height = (int)(packedSize & 0xFFFFFFFF);
            try
            {
                current.GetUINT32(DefaultStrideKey, out var stride);
                _stride = stride;
            }
            catch (COMException)
            {
                _stride = 0; // unknown: derived from the buffer
            }
        }
        finally
        {
            Marshal.ReleaseComObject(current);
        }
    }

    private static TimeSpan ReadDuration(IMFSourceReader reader)
    {
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            for (var i = 0; i < 24; i++)
                Marshal.WriteByte(variant, i, 0);
            reader.GetPresentationAttribute(MediaSource, DurationKey, variant);
            return TimeSpan.FromTicks(Marshal.ReadInt64(variant, 8)); // VT_UI8, 100 ns units
        }
        catch (COMException)
        {
            return TimeSpan.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
        }
    }

    /// <summary>2-D view of a video buffer: gives the real row pitch (decoders pad rows).</summary>
    [ComImport]
    [Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMF2DBuffer
    {
        void Lock2D(out IntPtr scanline0, out int pitch);

        void Unlock2D();

        void GetScanline0AndPitch(out IntPtr scanline0, out int pitch);

        void IsContiguousFormat([MarshalAs(UnmanagedType.Bool)] out bool contiguous);

        void GetContiguousLength(out int length);

        void ContiguousCopyTo(IntPtr destination, int length);

        void ContiguousCopyFrom(IntPtr source, int length);
    }
}
