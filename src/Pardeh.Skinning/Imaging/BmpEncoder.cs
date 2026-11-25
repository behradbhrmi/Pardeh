using System.Buffers.Binary;

namespace Pardeh.Skinning.Imaging;

/// <summary>
/// Writes a <see cref="SkinBitmap"/> as an uncompressed 24-bit Windows BMP (what <see cref="BmpDecoder"/> reads back).
/// Used by skin loaders that cut one sheet into the per-state images the skin engine expects, and by tests that
/// draw synthetic skins. Transparency is not kept: BMP has none.
/// </summary>
public static class BmpEncoder
{
    public static byte[] Encode(SkinBitmap bitmap)
    {
        var stride = (bitmap.Width * 3 + 3) & ~3;
        var dataSize = stride * bitmap.Height;
        var file = new byte[54 + dataSize];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), bitmap.Width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), bitmap.Height); // positive: rows are stored bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(34), dataSize);

        for (var y = 0; y < bitmap.Height; y++)
        {
            var source = y * bitmap.Stride;
            var target = 54 + (bitmap.Height - 1 - y) * stride;
            for (var x = 0; x < bitmap.Width; x++, source += 4, target += 3)
            {
                file[target] = bitmap.Pixels[source];
                file[target + 1] = bitmap.Pixels[source + 1];
                file[target + 2] = bitmap.Pixels[source + 2];
            }
        }

        return file;
    }
}
