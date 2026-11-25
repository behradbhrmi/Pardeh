using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pardeh.Skinning.Imaging;

namespace Pardeh.UI.Rendering;

/// <summary>Converts skin bitmaps to Avalonia bitmaps once and caches them.</summary>
internal static class BitmapBridge
{
    /// <summary>Skia's native pixel order is BGRA on Windows but RGBA on Android: copied pixels are put in BGRA order (the skin bitmaps' order).</summary>
    public static void ToBgra(byte[] pixels, PixelFormat? format)
    {
        if (format != PixelFormat.Rgba8888)
            return;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
    }

    private static readonly ConditionalWeakTable<SkinBitmap, Bitmap> Cache = new();

    public static Bitmap ToAvalonia(SkinBitmap source) => Cache.GetValue(source, Create);

    private static Bitmap Create(SkinBitmap source)
    {
        var bitmap = new WriteableBitmap(new PixelSize(source.Width, source.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < source.Height; y++)
            Marshal.Copy(source.Pixels, y * source.Stride, buffer.Address + y * buffer.RowBytes, source.Stride);
        return bitmap;
    }
}

/// <summary>Decodes PNG, JPEG and GIF skin images through Avalonia (BMP uses the managed decoder).</summary>
internal sealed class AvaloniaImageDecoder : IImageDecoder
{
    public SkinBitmap? Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            using var stream = new MemoryStream(data.ToArray());
            using var decoded = new Bitmap(stream);
            var width = decoded.PixelSize.Width;
            var height = decoded.PixelSize.Height;
            var pixels = new byte[width * height * 4];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                decoded.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
            }
            finally
            {
                handle.Free();
            }

            BitmapBridge.ToBgra(pixels, decoded.Format);
            return new SkinBitmap(width, height, pixels);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
