using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>PARDEH_LYRICS_ICONS=&lt;file.png&gt; draws the lyrics icon candidates on skin-like backgrounds at 96, 44 and 24 units (visual check only).</summary>
[Collection("Avalonia rendering")]
public sealed class LyricsIconSheet
{
    private sealed class Sheet : Control
    {
        public string[] Keys = [];
        public override void Render(DrawingContext g)
        {
            string[] backs = ["#0F3A9F", "#1C4DAA", "#0B0E1C", "#E9EFFA", "#2B2B30"];
            for (var r = 0; r < backs.Length; r++)
            {
                g.DrawRectangle(new SolidColorBrush(Color.Parse(backs[r])), null, new Rect(0, r * 110, 900, 110));
                for (var k = 0; k < Keys.Length; k++)
                {
                    var x = 10 + k * 220;
                    PictorialIcons.Draw(g, Keys[k], new Rect(x, r * 110 + 5, 96, 96));
                    PictorialIcons.Draw(g, Keys[k], new Rect(x + 110, r * 110 + 30, 44, 44));
                    PictorialIcons.Draw(g, Keys[k], new Rect(x + 165, r * 110 + 40, 24, 24));
                }
            }
        }
    }

    [Fact]
    public void Icon_candidates_sheet()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_LYRICS_ICONS") is not { Length: > 0 } file)
            return;
        TestPlatform.Ensure();

        var keys = (Environment.GetEnvironmentVariable("PARDEH_LYRICS_KEYS") ?? "lyrics,note,heart").Split(',');
        var sheet = new Sheet { Keys = keys };
        sheet.Measure(new Size(900, 550));
        sheet.Arrange(new Rect(0, 0, 900, 550));
        using var bitmap = new RenderTargetBitmap(new PixelSize(900, 550), new Vector(96, 96));
        bitmap.Render(sheet);
        bitmap.Save(file);
    }
}
