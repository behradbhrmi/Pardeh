using System.Text;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Tests;

/// <summary>Behaviour the classic player's compiled skins rely on (no private files needed).</summary>
public class CompiledSkinTests
{
    private static (Pardeh.Skinning.Runtime.SkinSession Session, Pardeh.Skinning.Runtime.SkinView View) Run(string wms)
    {
        var package = WmzLoader.FromFiles(new() { ["t.wms"] = Encoding.UTF8.GetBytes(wms) }, "memory://t");
        var (session, _, _, _) = TestSkins.Start(package);
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session, session.CurrentView!);
    }

    [Fact]
    public void Live_expressions_use_values_bound_after_the_one_time_pass()
    {
        // svAppBottomRight in the full mode: its top depends on a height that a wmpprop: binding sets later.
        var (_, view) = Run("""
            <THEME><VIEW width="690" height="500">
              <SUBVIEW id="app" width="wmpprop:view.width" height="wmpprop:view.height">
                <SUBVIEW id="corner" top="JScriptLive:app.height-79" left="JScriptLive:app.width-57" width="57" height="79"/>
              </SUBVIEW>
            </VIEW></THEME>
            """);

        var corner = view.FindById("corner")!;
        Assert.Equal(421, corner.Top);
        Assert.Equal(633, corner.Left);
    }

    [Fact]
    public void Button_group_without_background_draws_only_its_buttons()
    {
        var (_, view) = Run("""
            <THEME><VIEW width="100" height="50">
              <BUTTONGROUP id="g" image="a.bmp" mappingImage="m.bmp" showBackground="false">
                <BUTTONELEMENT mappingColor="#FF0000"/>
              </BUTTONGROUP>
            </VIEW></THEME>
            """);

        var group = (ButtonGroupElement)view.FindById("g")!;
        Assert.False(group.ShowBackground);
    }

    [Fact]
    public void Hue_shift_rotates_colour_and_keeps_transparency()
    {
        // Blue (240°) + 240° = 480° = 120°: green, as the "green" colour preset does to the chrome.
        var green = Pardeh.Skinning.Imaging.SkinBitmap.ShiftColor(0x0000FF, 240, 1);
        Assert.Equal(0x00FF00, green);
        var grey = Pardeh.Skinning.Imaging.SkinBitmap.ShiftColor(0x3050A0, 0, 0);
        Assert.Equal((grey >> 16) & 0xFF, grey & 0xFF);

        var bitmap = new Pardeh.Skinning.Imaging.SkinBitmap(2, 1, [255, 0, 0, 255, 0, 0, 0, 0]).WithHueSaturation(120, 1);
        Assert.Equal(0, bitmap.GetAlpha(1, 0));
        Assert.Equal(255, bitmap.GetAlpha(0, 0));
    }

    [Fact]
    public void Calc_layout_reserves_room_for_enhancements_when_shown()
    {
        var (session, _) = Run("""<THEME><VIEW width="100" height="50"/></THEME>""");
        var helper = session.Helper;
        session.MediaCenter.SetMember("showSettings", true);
        helper.InvokeMember("calcLayout", [588.0, 385.0, false, false]);
        Assert.Equal(120.0, helper.InvokeMember("getLayoutSize", [1.0]));
        Assert.Equal(385.0 - 20 - 120 - 2, helper.InvokeMember("getLayoutSize", [5.0]));

        session.MediaCenter.SetMember("showSettings", false);
        helper.InvokeMember("calcLayout", [588.0, 385.0, false, false]);
        Assert.Equal(0.0, helper.InvokeMember("getLayoutSize", [1.0]));
    }

    [Fact]
    public void Colour_chooser_recolours_the_app_colours()
    {
        var (session, _) = Run("""<THEME><VIEW width="100" height="50"/></THEME>""");
        var before = session.MediaCenter.GetMember("appColorMedium");
        session.MediaCenter.SetMember("backHueShift", 240.0);
        Assert.NotEqual(before, session.MediaCenter.GetMember("appColorMedium"));
        session.MediaCenter.SetMember("backHueShift", 0.0);
        Assert.Equal("#394472", session.MediaCenter.GetMember("appColorMedium"));
    }
}
