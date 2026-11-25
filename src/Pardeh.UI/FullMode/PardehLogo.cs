using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Pardeh.UI.FullMode;

/// <summary>
/// Pardeh's logo: a picture (the blue curtains by default, <see cref="PardehLogoArt.Default"/>; the PNG masters are embedded
/// from src/Pardeh.UI/Assets, see <see cref="LogoImage"/>), drawn from a pre-filtered copy of the right size
/// at any size and in any art density. If the default is one of the older vector designs (or the picture cannot be decoded) the vector
/// art of <see cref="PardehLogoArt"/> is drawn instead. A personal picture can replace it all: %APPDATA%\Pardeh\logo.png (that folder
/// is never committed or packaged).
/// </summary>
internal static class PardehLogo
{
    private static readonly Lazy<Bitmap?> Override = new(() =>
    {
        var path = Path.Combine(AppPaths.DataDirectory, "logo.png");
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    });

    public static void Draw(DrawingContext g, Rect r)
    {
        if (Override.Value is { } picture)
        {
            g.DrawImage(picture, r);
            return;
        }

        if (PardehLogoArt.IsImage(PardehLogoArt.Default) && LogoImage.Get(PardehLogoArt.Default) is { } image)
        {
            if (AppSettings.Current.LogoFollowsSkinColor)
            {
                var tint = Rendering.GlassPalette.Current;
                image = image.WithHue(tint.Hue, tint.Saturation, tint.Brightness);
            }

            image.Draw(g, r);
            return;
        }

        PardehLogoArt.Full.Draw(g, r);
    }
}
