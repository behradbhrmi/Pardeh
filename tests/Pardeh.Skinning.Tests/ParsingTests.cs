using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime;

namespace Pardeh.Skinning.Tests;

public class ParsingTests
{
    [Fact]
    public void Lenient_parser_accepts_bare_ampersands_and_comments()
    {
        var doc = LenientXmlParser.Parse("""
            <?xml version="1.0"?>
            <!-- a comment -->
            <THEME id="t">
              <VIEW id="v" width="100" visible="JScript:a && b">
                <BUTTON id="b" onclick="x = 1 &lt; 2;"/>
              </VIEW>
            </THEME>
            """);

        var theme = Assert.Single(doc.Children);
        var view = Assert.Single(theme.Children);
        Assert.Equal("JScript:a && b", view.GetAttribute("VISIBLE"));
        Assert.Equal("x = 1 < 2;", view.Children[0].GetAttribute("onClick"));
    }

    [Theory]
    [InlineData("JScript:view.width-10;", AttributeValueKind.Script, "view.width-10")]
    [InlineData("jscript:a", AttributeValueKind.Script, "a")]
    [InlineData("wmpprop:player.settings.mute", AttributeValueKind.Binding, "player.settings.mute")]
    [InlineData("wmpenabled:player.controls.pause", AttributeValueKind.Binding, "player.controls.isAvailable('pause')")]
    [InlineData("42", AttributeValueKind.Literal, "42")]
    public void Classifies_attribute_values(string value, AttributeValueKind kind, string expression)
    {
        Assert.Equal(kind, SkinValue.Classify(value, out var actual));
        Assert.Equal(expression, actual);
    }

    [Fact]
    public void Bmp_decoder_reads_24_bit_bottom_up_images_and_color_keys()
    {
        // 2x2, 24-bit: bottom row blue, magenta; top row red, green.
        byte[] pixelData =
        [
            0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00, // bottom row (B,G,R) + 2 bytes padding
            0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00, // top row
        ];
        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(54 + pixelData.Length).CopyTo(header, 2);
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(2).CopyTo(header, 18);
        BitConverter.GetBytes(2).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)24).CopyTo(header, 28);

        var bitmap = new BmpDecoder().Decode([.. header, .. pixelData])!;

        Assert.Equal(0xFF0000, bitmap.GetRgb(0, 0));
        Assert.Equal(0x00FF00, bitmap.GetRgb(1, 0));
        Assert.Equal(0x0000FF, bitmap.GetRgb(0, 1));
        Assert.Equal(0xFF00FF, bitmap.GetRgb(1, 1));

        var keyed = bitmap.WithColorKey(0xFF00FF);
        Assert.Equal(0, keyed.GetAlpha(1, 1));
        Assert.Equal(255, keyed.GetAlpha(0, 1));
    }
}
