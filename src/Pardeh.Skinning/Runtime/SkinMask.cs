using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.Skinning.Runtime;

/// <summary>
/// Which pixels of a view are part of the skin. Used to shape the window so clicks on transparent
/// areas fall through to whatever is behind it, as classic skinned players did.
/// </summary>
public sealed class SkinMask
{
    private SkinMask(int width, int height, bool[] opaque)
    {
        Width = width;
        Height = height;
        Opaque = opaque;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major, Width * Height.</summary>
    public bool[] Opaque { get; }

    public bool this[int x, int y] => (uint)x < (uint)Width && (uint)y < (uint)Height && Opaque[y * Width + x];

    /// <summary>Horizontal runs of opaque pixels per row: (y, xStart, xEnd exclusive).</summary>
    public IEnumerable<(int Y, int X0, int X1)> Runs()
    {
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            var x = 0;
            while (x < Width)
            {
                while (x < Width && !Opaque[row + x])
                    x++;
                var start = x;
                while (x < Width && Opaque[row + x])
                    x++;
                if (x > start)
                    yield return (y, start, x);
            }
        }
    }

    public static SkinMask Compute(SkinView view)
    {
        var width = Math.Max(1, (int)Math.Ceiling(view.Root.Width));
        var height = Math.Max(1, (int)Math.Ceiling(view.Root.Height));
        var mask = new SkinMask(width, height, new bool[width * height]);
        mask.Add(view.Root, 0, 0, new Clip(0, 0, width, height));
        return mask;
    }

    private readonly record struct Clip(int X0, int Y0, int X1, int Y1)
    {
        public Clip Intersect(int x0, int y0, int x1, int y1) =>
            new(Math.Max(X0, x0), Math.Max(Y0, y0), Math.Min(X1, x1), Math.Min(Y1, y1));

        public bool IsEmpty => X1 <= X0 || Y1 <= Y0;
    }

    private void Add(SkinElement element, int originX, int originY, Clip parentClip)
    {
        if (!element.Visible || !element.IsVisual)
            return;

        var x = element is ViewElement ? 0 : originX + (int)Math.Round(element.Left);
        var y = element is ViewElement ? 0 : originY + (int)Math.Round(element.Top);
        var w = (int)Math.Ceiling(element.Width);
        var h = (int)Math.Ceiling(element.Height);
        var clip = element is TextElement && w <= 0 ? parentClip : parentClip.Intersect(x, y, x + w, y + h);
        if (clip.IsEmpty)
            return;

        switch (element)
        {
            case ViewElement view:
                if (view.BackgroundColor is not null)
                    FillRect(clip);
                AddBitmap(view.BackgroundImage, x, y, clip, view.BackgroundTiled);
                break;
            case SubviewElement subview:
                if (subview.BackgroundColor is not null)
                    FillRect(clip);
                AddBitmap(subview.BackgroundImage, x, y, clip, subview.BackgroundTiled);
                break;
            case ButtonElement button:
                var image = button.CurrentImage;
                if (image is null)
                    FillRect(clip);
                else
                    AddBitmap(image, x, y, clip, tiled: button.GetBool("tiled"));
                break;
            case ButtonGroupElement group when group.ShowBackground:
                AddBitmap(group.BaseImage, x, y, clip, tiled: false);
                break;
            case SliderElement slider:
                AddBitmap(slider.TrackImage, x, y, clip, slider.Tiled);
                var thumb = slider.ThumbRect;
                AddBitmap(slider.ThumbImage, x + (int)thumb.X, y + (int)thumb.Y, clip, tiled: false);
                break;
            case TextElement text:
                if (text.BackgroundColor is not null)
                    FillRect(clip);
                break;
            case VideoElement or EffectsElement or PlaylistElement or DropDownPlaylistElement or LibraryElement or Panes.ListPaneElement or HostAreaElement { Embedded: not null }:
                FillRect(clip);
                break;
            case GenericElement generic:
                if (generic.BackgroundColor is not null)
                    FillRect(clip);
                break;
        }

        foreach (var child in SkinView.PaintOrder(element))
            Add(child, x, y, clip);
    }

    private void FillRect(Clip clip)
    {
        for (var py = Math.Max(0, clip.Y0); py < Math.Min(Height, clip.Y1); py++)
            Array.Fill(Opaque, true, py * Width + Math.Max(0, clip.X0), Math.Max(0, Math.Min(Width, clip.X1) - Math.Max(0, clip.X0)));
    }

    private void AddBitmap(SkinBitmap? bitmap, int x, int y, Clip clip, bool tiled)
    {
        if (bitmap is null)
            return;

        var x0 = Math.Max(0, clip.X0);
        var y0 = Math.Max(0, clip.Y0);
        var x1 = Math.Min(Width, clip.X1);
        var y1 = Math.Min(Height, clip.Y1);
        // The mask is in skin units; a high-resolution image (Scale > 1) is sampled at the centre of each unit's pixel block.
        var scale = bitmap.Scale;
        var unitsW = Math.Max(1, (int)Math.Ceiling(bitmap.LogicalWidth));
        var unitsH = Math.Max(1, (int)Math.Ceiling(bitmap.LogicalHeight));
        if (!tiled)
        {
            x1 = Math.Min(x1, x + unitsW);
            y1 = Math.Min(y1, y + unitsH);
        }

        for (var py = y0; py < y1; py++)
        {
            var by = py - y;
            if (by < 0)
                continue;
            if (tiled)
                by %= unitsH;
            var row = py * Width;
            for (var px = x0; px < x1; px++)
            {
                var bx = px - x;
                if (bx < 0)
                    continue;
                if (tiled)
                    bx %= unitsW;
                var sx = Math.Min(bitmap.Width - 1, bx * scale + scale / 2);
                var sy = Math.Min(bitmap.Height - 1, by * scale + scale / 2);
                if (bitmap.Pixels[(sy * bitmap.Width + sx) * 4 + 3] != 0)
                    Opaque[row + px] = true;
            }
        }
    }
}
