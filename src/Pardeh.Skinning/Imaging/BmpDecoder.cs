using System.Buffers.Binary;

namespace Pardeh.Skinning.Imaging;

/// <summary>
/// Windows BMP decoder for the formats skins use: uncompressed 1/4/8/16/24/32-bit, plus RLE8 / RLE4 compressed
/// 8 / 4-bit images (Winamp classic skins use RLE8 for most of their sheets). Pure managed code so it works on every platform.
/// </summary>
public sealed class BmpDecoder : IImageDecoder
{
    private const int MaxDimension = 16384;

    public SkinBitmap? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || data[0] != (byte)'B' || data[1] != (byte)'M')
            return null;

        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);

        int width, height, bitCount, compression = 0, colorsUsed = 0;
        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadInt16LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(data[20..]);
            bitCount = BinaryPrimitives.ReadInt16LittleEndian(data[24..]);
        }
        else
        {
            if (data.Length < 54)
                return null;
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
            bitCount = BinaryPrimitives.ReadInt16LittleEndian(data[28..]);
            compression = BinaryPrimitives.ReadInt32LittleEndian(data[30..]);
            colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(data[46..]);
        }

        var topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
            return null;

        // 0 = BI_RGB, 3 = BI_BITFIELDS (we assume the standard 565/888 masks), 1 = BI_RLE8, 2 = BI_RLE4.
        if (compression is not (0 or 1 or 2 or 3))
            return null;
        if ((compression == 1 && bitCount != 8) || (compression == 2 && bitCount != 4) || (compression is 1 or 2 && topDown))
            return null;

        // Palette for indexed formats.
        var paletteEntrySize = headerSize == 12 ? 3 : 4;
        int[]? palette = null;
        if (bitCount <= 8)
        {
            var count = colorsUsed > 0 ? colorsUsed : 1 << bitCount;
            palette = new int[count];
            var paletteStart = 14 + headerSize;
            for (var i = 0; i < count; i++)
            {
                var p = paletteStart + i * paletteEntrySize;
                if (p + 2 >= data.Length)
                    break;
                palette[i] = (data[p + 2] << 16) | (data[p + 1] << 8) | data[p];
            }
        }

        var stride = ((width * bitCount + 31) / 32) * 4;
        byte[]? rle = null; // palette indices, one byte per pixel, rows bottom-up like the file
        if (compression is 1 or 2)
        {
            if (pixelOffset < 0 || pixelOffset > data.Length)
                return null;
            rle = DecodeRle(data[pixelOffset..], width, height, compression == 2);
        }
        else if (pixelOffset < 0 || pixelOffset + (long)stride * height > data.Length)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var srcRow = rle is null ? data.Slice(pixelOffset + (topDown ? row : height - 1 - row) * stride, stride) : [];
            var dst = row * width * 4;
            for (var x = 0; x < width; x++, dst += 4)
            {
                int rgb;
                switch (bitCount)
                {
                    case 32:
                        rgb = (srcRow[x * 4 + 2] << 16) | (srcRow[x * 4 + 1] << 8) | srcRow[x * 4];
                        break;
                    case 24:
                        rgb = (srcRow[x * 3 + 2] << 16) | (srcRow[x * 3 + 1] << 8) | srcRow[x * 3];
                        break;
                    case 16:
                        {
                            var v = BinaryPrimitives.ReadUInt16LittleEndian(srcRow[(x * 2)..]);
                            int r, g, b;
                            if (compression == 3)
                            {
                                r = (v >> 11) & 0x1F; g = (v >> 5) & 0x3F; b = v & 0x1F;
                                r = r << 3 | r >> 2; g = g << 2 | g >> 4; b = b << 3 | b >> 2;
                            }
                            else
                            {
                                r = (v >> 10) & 0x1F; g = (v >> 5) & 0x1F; b = v & 0x1F;
                                r = r << 3 | r >> 2; g = g << 3 | g >> 2; b = b << 3 | b >> 2;
                            }

                            rgb = (r << 16) | (g << 8) | b;
                            break;
                        }
                    case 8 when rle is not null:
                    case 4 when rle is not null:
                        rgb = Lookup(palette!, rle[(height - 1 - row) * width + x]);
                        break;
                    case 8:
                        rgb = Lookup(palette!, srcRow[x]);
                        break;
                    case 4:
                        rgb = Lookup(palette!, (srcRow[x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0x0F);
                        break;
                    case 1:
                        rgb = Lookup(palette!, (srcRow[x / 8] >> (7 - x % 8)) & 0x01);
                        break;
                    default:
                        return null;
                }

                pixels[dst] = (byte)rgb;
                pixels[dst + 1] = (byte)(rgb >> 8);
                pixels[dst + 2] = (byte)(rgb >> 16);
                pixels[dst + 3] = 255;
            }
        }

        return new SkinBitmap(width, height, pixels);
    }

    private static int Lookup(int[] palette, int index) => index < palette.Length ? palette[index] : 0;

    /// <summary>
    /// Expands RLE8 / RLE4 data into one palette index per pixel (rows bottom-up like the file; untouched pixels stay 0).
    /// Encoded runs, absolute runs, end-of-line, end-of-bitmap and delta escapes; truncated or out-of-range data stops
    /// the decode (what was drawn so far is kept) rather than failing.
    /// </summary>
    private static byte[] DecodeRle(ReadOnlySpan<byte> data, int width, int height, bool fourBit)
    {
        var indices = new byte[width * height];
        int x = 0, y = 0, i = 0;

        void Put(int value)
        {
            if (x < width && y < height)
                indices[y * width + x] = (byte)value;
            x++;
        }

        while (i + 1 < data.Length && y < height)
        {
            int count = data[i], value = data[i + 1];
            i += 2;
            if (count > 0)
            {
                // Encoded run: RLE8 repeats the byte, RLE4 alternates its two nibbles.
                for (var n = 0; n < count; n++)
                    Put(fourBit ? (n % 2 == 0 ? value >> 4 : value & 0x0F) : value);
                continue;
            }

            switch (value)
            {
                case 0: // end of line
                    x = 0;
                    y++;
                    break;
                case 1: // end of bitmap
                    return indices;
                case 2: // delta: move right and up (the file is bottom-up, so y grows)
                    if (i + 1 >= data.Length)
                        return indices;
                    x += data[i];
                    y += data[i + 1];
                    i += 2;
                    break;
                default: // absolute run of 'value' pixels, padded to a 16-bit boundary
                    var bytes = fourBit ? (value + 1) / 2 : value;
                    if (i + bytes > data.Length)
                        return indices;
                    for (var n = 0; n < value; n++)
                        Put(fourBit ? (n % 2 == 0 ? data[i + n / 2] >> 4 : data[i + n / 2] & 0x0F) : data[i + n]);
                    i += bytes + (bytes & 1);
                    break;
            }
        }

        return indices;
    }
}
