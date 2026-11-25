namespace Pardeh.Core.Playback;

/// <summary>A decoded video frame: 32-bit BGRA, opaque, top-down rows, stride = Width * 4.</summary>
public sealed class VideoFrame(int width, int height, byte[] pixels, long sequence)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public byte[] Pixels { get; } = pixels;

    /// <summary>Increases with every new frame, so renderers can skip unchanged frames.</summary>
    public long Sequence { get; } = sequence;

    /// <summary>The picture's own time in the stream.</summary>
    public TimeSpan Time { get; init; }
}
