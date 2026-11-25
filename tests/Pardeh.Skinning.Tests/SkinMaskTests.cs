using Pardeh.Skinning.Runtime;

namespace Pardeh.Skinning.Tests;

public class SkinMaskTests
{
    [Fact]
    public void Corona_mask_covers_the_player_and_not_the_empty_drawer_area()
    {
        var (session, _, _, _) = TestSkins.Start(TestSkins.LoadPrivate("9SeriesDefault.wmz"));
        var mask = SkinMask.Compute(session.CurrentView!);

        Assert.Equal(859, mask.Width);
        Assert.Equal(468, mask.Height);
        Assert.True(mask[400, 150], "video area should be opaque");
        Assert.True(mask[300, 310], "transport bar should be opaque");
        Assert.False(mask[20, 20], "hidden playlist drawer area should be transparent");
        Assert.False(mask[850, 460], "bottom-right corner should be transparent");
        var opaque = mask.Opaque.Count(o => o);
        Assert.InRange(opaque, 60_000, 200_000);
    }
}
