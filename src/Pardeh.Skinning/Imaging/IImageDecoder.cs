namespace Pardeh.Skinning.Imaging;

/// <summary>Decodes image bytes into a <see cref="SkinBitmap"/>. Returns null if the format isn't supported.</summary>
public interface IImageDecoder
{
    SkinBitmap? Decode(ReadOnlySpan<byte> data);
}

/// <summary>Tries a list of decoders in order.</summary>
public sealed class CompositeImageDecoder(params IImageDecoder[] decoders) : IImageDecoder
{
    public SkinBitmap? Decode(ReadOnlySpan<byte> data)
    {
        foreach (var decoder in decoders)
        {
            var bitmap = decoder.Decode(data);
            if (bitmap is not null)
                return bitmap;
        }

        return null;
    }
}
