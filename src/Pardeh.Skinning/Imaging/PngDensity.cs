namespace Pardeh.Skinning.Imaging;

/// <summary>
/// The opt-in density marker of high-resolution skin art: a private ancillary PNG chunk <c>pdSc</c> (one byte, image
/// pixels per skin unit) placed right after <c>IHDR</c>. A PNG with the chunk decodes to a <see cref="SkinBitmap"/> of
/// that <see cref="SkinBitmap.Scale"/>; every PNG without it (all Windows skins, extracted XP art, <c>.wsz</c>) stays at
/// Scale 1 and renders exactly as before. Other PNG readers ignore the chunk (it is ancillary and safe to copy).
/// Deliberately not the standard <c>pHYs</c> chunk: ordinary skins carry 72 / 96 / 300 dpi values that mean nothing here.
/// </summary>
public static class PngDensity
{
    private const string ChunkType = "pdSc";
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The density stored in the PNG (1 when it has no marker, or is not a PNG).</summary>
    public static int Read(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(Signature))
            return 1;

        var position = 8;
        while (position + 12 <= png.Length)
        {
            var length = (png[position] << 24) | (png[position + 1] << 16) | (png[position + 2] << 8) | png[position + 3];
            if (length < 0 || position + 12L + length > png.Length)
                return 1;
            var type = png.Slice(position + 4, 4);
            if (type[0] == 'I' && type[1] == 'D' && type[2] == 'A' && type[3] == 'T')
                return 1; // the marker, when present, sits before the image data
            if (type[0] == 'p' && type[1] == 'd' && type[2] == 'S' && type[3] == 'c')
                return length == 1 ? Math.Clamp((int)png[position + 8], 1, 8) : 1;
            position += 12 + length;
        }

        return 1;
    }

    /// <summary>Returns <paramref name="png"/> with the density marker added (unchanged for scale 1 or a malformed file).</summary>
    public static byte[] Mark(byte[] png, int scale)
    {
        if (scale <= 1 || png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(Signature))
            return png;

        // IHDR is always the first chunk: 4 (length) + 4 (type) + 13 (data) + 4 (crc).
        const int AfterHeader = 8 + 25;
        var marked = new byte[png.Length + 13];
        Buffer.BlockCopy(png, 0, marked, 0, AfterHeader);
        var at = AfterHeader;
        marked[at + 3] = 1;
        marked[at + 4] = (byte)'p';
        marked[at + 5] = (byte)'d';
        marked[at + 6] = (byte)'S';
        marked[at + 7] = (byte)'c';
        marked[at + 8] = (byte)Math.Min(scale, 8);
        var crc = Crc32(marked.AsSpan(at + 4, 5));
        marked[at + 9] = (byte)(crc >> 24);
        marked[at + 10] = (byte)(crc >> 16);
        marked[at + 11] = (byte)(crc >> 8);
        marked[at + 12] = (byte)crc;
        Buffer.BlockCopy(png, AfterHeader, marked, AfterHeader + 13, png.Length - AfterHeader);
        return marked;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}
