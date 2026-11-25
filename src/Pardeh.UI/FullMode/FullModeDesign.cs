using Avalonia.Media;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The look of one of Pardeh's designed full modes (see <see cref="FullModeDesigner"/>): palette, fonts and a few
/// shape choices. The layout (top bar, task rail, stage, playlist panel, transport) is shared.
/// </summary>
internal sealed record FullModeDesign
{
    public required string Key { get; init; }
    public required string Name { get; init; }

    /// <summary>Flat Windows 11 drawing (no gloss or bevels, flat orbs, accent bars) instead of XP-era shading.</summary>
    public bool Modern { get; init; }

    /// <summary>The blue block behind the emblem and menus, with its sweeping corner (Horizon).</summary>
    public bool LogoBlock { get; init; }

    public string Font { get; init; } = "Tahoma";

    // Frame and top bar.
    public Color Outline { get; init; }
    public Color BarLight { get; init; }
    public Color BarDark { get; init; }
    public Color BarHighlight { get; init; } = Colors.White;
    public Color BarLine { get; init; }
    public Color LogoLight { get; init; }
    public Color LogoMid { get; init; }
    public Color LogoDark { get; init; }
    public string MenuInk { get; init; } = "#FFFFFF";
    public string MenuHover { get; init; } = "#FFE38A";
    public Color TabLight { get; init; } = Colors.White;
    public Color TabDark { get; init; }
    public Color TabBorder { get; init; }

    // Task rail.
    public Color RailLight { get; init; }
    public Color RailMid { get; init; }
    public Color RailDark { get; init; }
    public Color RailEdge { get; init; }
    public Color CapLight { get; init; }
    public Color CapDark { get; init; }
    public string CapInk { get; init; } = "#FFFFFF";
    public Color RailInk { get; init; }
    public Color PillLight { get; init; }
    public Color PillDark { get; init; }
    public Color PillBorder { get; init; }
    public Color HoverBorder { get; init; }

    // Transport.
    public Color Silver { get; init; }
    public Color SilverMid { get; init; }
    public Color SilverDark { get; init; }
    public Color SilverLine { get; init; }

    // Playlist panel and panes.
    public string Panel { get; init; } = "#485A97";
    public Color PanelDark { get; init; }
    public Color PanelDarker { get; init; }
    public Color PanelLight { get; init; }
    public Color HeaderTop { get; init; }
    public Color HeaderBottom { get; init; }
    public string PanelText { get; init; } = "#FFFFFF";
    public string PanelSub { get; init; } = "#D7E0F4";
    public string PanelAccent { get; init; } = "#A4C1F4";
    public string Rule { get; init; } = "#6276AD";
    public string InfoBack { get; init; } = "#1F2A5C";

    // Stage.
    public Color StageGlow { get; init; }
    public string StatusBack { get; init; } = "#31363A";
    public string StatusInk { get; init; } = "#00EE00";
    public string StatusSub { get; init; } = "#9FB3D9";
    public string TitleInk { get; init; } = "#FFFFFF";
    public string ArtistInk { get; init; } = "#C8D2E8";

    // LCD time capsule.
    public Color LcdTop { get; init; }
    public Color Lcd { get; init; }
    public Color LcdBorder { get; init; }
    public string LcdInk { get; init; } = "#00EE00";

    // Buttons.
    public Color Glyph { get; init; }
    public Color GlyphDisabled { get; init; }
    public Color OrbMid { get; init; }
    public Color OrbDark { get; init; }
    public Color OrbHoverMid { get; init; }
    public Color OrbHoverDark { get; init; }
    public Color OrbRim { get; init; }
    public Color OrbHeroRim { get; init; }
    public Color OrbHoverRim { get; init; }

    // Seek and volume.
    public Color SeekTop { get; init; }
    public Color SeekBottom { get; init; }
    public Color SeekLine { get; init; }
    public Color FillLight { get; init; }
    public Color FillMid { get; init; }
    public Color FillDark { get; init; }
    public string ThumbLight { get; init; } = "#5EE256";
    public string ThumbMid { get; init; } = "#15A50B";
    public string ThumbDark { get; init; } = "#0B6E06";
    public string ThumbHoverLight { get; init; } = "#FFD27A";
    public string ThumbHoverMid { get; init; } = "#F08A1C";
    public string ThumbHoverDark { get; init; } = "#B85600";
    public Color VolumeLight { get; init; }
    public Color VolumeDark { get; init; }

    public Color EdgeLight { get; init; }
    public Color EdgeDark { get; init; }

    public string SourcePath => "pardeh://full-mode/" + Key;

    private static Color C(string hex) => Color.Parse(hex);

    /// <summary>Horizon: inspired by the layout of the community WMP 9 web clone; light bar, blue logo block, sky rail.</summary>
    public static FullModeDesign Horizon { get; } = new()
    {
        Key = "horizon",
        Name = "Horizon",
        LogoBlock = true,
        Outline = C("#142A5C"), BarLight = C("#EFF4FB"), BarDark = C("#D6E3F4"), BarLine = C("#AABFE0"),
        LogoLight = C("#6A8CC2"), LogoMid = C("#5577B7"), LogoDark = C("#4566AA"),
        TabDark = C("#D5E1F3"), TabBorder = C("#93A9D8"),
        RailLight = C("#E2ECF8"), RailMid = C("#C9DBF1"), RailDark = C("#A4C0E4"), RailEdge = C("#7F9ACB"),
        CapLight = C("#5274B6"), CapDark = C("#3B5996"), RailInk = C("#1A326C"),
        PillLight = C("#EAF2FC"), PillDark = C("#ADCCEE"), PillBorder = C("#6F8CC6"), HoverBorder = C("#8FA9D8"),
        Silver = C("#F2F6FB"), SilverMid = C("#E3EBF7"), SilverDark = C("#C9D7EE"), SilverLine = C("#AABFE0"),
        PanelDark = C("#34477B"), PanelDarker = C("#2A3C6B"), PanelLight = C("#6276AD"), HeaderTop = C("#7186C0"), HeaderBottom = C("#4F639F"),
        StageGlow = C("#22356F"),
        LcdTop = C("#2A3646"), Lcd = C("#1D2733"), LcdBorder = C("#0E1520"),
        Glyph = C("#22356E"), GlyphDisabled = C("#A3ADC6"),
        OrbMid = C("#E4ECF8"), OrbDark = C("#B9C9E6"), OrbHoverMid = C("#D8EBFF"), OrbHoverDark = C("#9FCBFA"),
        OrbRim = C("#6F87C2"), OrbHeroRim = C("#3F5FA8"), OrbHoverRim = C("#2F7FE0"),
        SeekTop = C("#B8C9E0"), SeekBottom = C("#E4ECF6"), SeekLine = C("#8C9FC6"),
        FillLight = C("#DDF3CC"), FillMid = C("#A9D883"), FillDark = C("#7DB352"),
        VolumeLight = C("#B8FFB0"), VolumeDark = C("#2FCF2A"),
        EdgeLight = C("#7B9CCF"), EdgeDark = C("#4667A4"),
    };

    /// <summary>
    /// Luna: the polished version of Pardeh Classic. Its periwinkle-and-navy palette and dark menus on the band,
    /// with Horizon's richer layout (cover and details, Info Center, every task working) and finer chrome.
    /// </summary>
    public static FullModeDesign Luna { get; } = new()
    {
        Key = "luna",
        Name = "Luna",
        LogoBlock = false,
        MenuInk = "#13235E",
        MenuHover = "#FFFFFF",
        Outline = C("#3B4C96"), BarLight = C("#CCD9F6"), BarDark = C("#8199D6"), BarLine = C("#5A6FB6"),
        LogoLight = C("#CCD9F6"), LogoMid = C("#A9BCEB"), LogoDark = C("#8199D6"),
        TabDark = C("#C3CFEE"), TabBorder = C("#8C9FD6"),
        RailLight = C("#FBFCFE"), RailMid = C("#E3EBF8"), RailDark = C("#BFD2EA"), RailEdge = C("#9DB2E4"),
        CapLight = C("#8CA6E3"), CapDark = C("#6C84C8"), RailInk = C("#212741"),
        PillLight = C("#FFFFFF"), PillDark = C("#DCE5F7"), PillBorder = C("#9FB4E4"), HoverBorder = C("#B8C8EC"),
        Silver = C("#F6F8FE"), SilverMid = C("#E2E8F7"), SilverDark = C("#BFCAE8"), SilverLine = C("#A5B5E0"),
        Panel = "#394472", PanelDark = C("#2E375D"), PanelDarker = C("#242B4A"), PanelLight = C("#6071BF"),
        HeaderTop = C("#7787D0"), HeaderBottom = C("#5465AE"),
        PanelSub = "#D3DAF2", PanelAccent = "#A9B8EC", Rule = "#55639F", InfoBack = "#212741",
        StageGlow = C("#1D2550"), StatusBack = "#0B0E1C", StatusInk = "#7FFF00", StatusSub = "#8F9CC9",
        ArtistInk = "#B8C3E6",
        LcdTop = C("#1B2244"), Lcd = C("#0E1430"), LcdBorder = C("#060918"), LcdInk = "#7FFF00",
        Glyph = C("#1F2F6E"), GlyphDisabled = C("#A3ABC4"),
        OrbMid = C("#E3E9F8"), OrbDark = C("#C3CEEC"), OrbHoverMid = C("#DCEBFF"), OrbHoverDark = C("#A9CBF6"),
        OrbRim = C("#7F93CC"), OrbHeroRim = C("#5666AB"), OrbHoverRim = C("#4F9BE8"),
        SeekTop = C("#AEB5CE"), SeekBottom = C("#D9DEEC"), SeekLine = C("#8E97B4"),
        FillLight = C("#C8E9C6"), FillMid = C("#94C692"), FillDark = C("#6FAE6C"),
        ThumbLight = "#7CE07A", ThumbMid = "#1AB919", ThumbDark = "#0E7C10",
        VolumeLight = C("#C8E9C6"), VolumeDark = C("#5FA85C"),
        EdgeLight = C("#8DA3DE"), EdgeDark = C("#5666AB"),
    };

    /// <summary>Fluent: the modern-UI full mode, a dark Windows 11 design with flat surfaces and an accent colour.</summary>
    public static FullModeDesign Fluent { get; } = new()
    {
        Key = "fluent",
        Name = "Fluent",
        Modern = true,
        LogoBlock = false,
        Font = "Segoe UI",
        MenuInk = "#FFFFFF",
        MenuHover = "#60CDFF",
        Outline = C("#3A3A3A"), BarLight = C("#202020"), BarDark = C("#202020"), BarHighlight = C("#202020"), BarLine = C("#2E2E2E"),
        LogoLight = C("#202020"), LogoMid = C("#202020"), LogoDark = C("#202020"),
        TabLight = C("#202020"), TabDark = C("#202020"), TabBorder = C("#202020"),
        RailLight = C("#1C1C1C"), RailMid = C("#1C1C1C"), RailDark = C("#1C1C1C"), RailEdge = C("#2E2E2E"),
        CapLight = C("#1C1C1C"), CapDark = C("#1C1C1C"), CapInk = "#9D9D9D", RailInk = C("#FFFFFF"),
        PillLight = C("#2D2D2D"), PillDark = C("#2D2D2D"), PillBorder = C("#60CDFF"), HoverBorder = C("#262626"),
        Silver = C("#202020"), SilverMid = C("#202020"), SilverDark = C("#202020"), SilverLine = C("#2E2E2E"),
        Panel = "#272727", PanelDark = C("#232323"), PanelDarker = C("#232323"), PanelLight = C("#3A3A3A"),
        HeaderTop = C("#272727"), HeaderBottom = C("#272727"),
        PanelSub = "#C5C5C5", PanelAccent = "#9D9D9D", Rule = "#3A3A3A", InfoBack = "#1C1C1C",
        StageGlow = C("#14181E"), StatusBack = "#1C1C1C", StatusInk = "#C5C5C5", StatusSub = "#8A8A8A",
        ArtistInk = "#C5C5C5",
        LcdTop = C("#2D2D2D"), Lcd = C("#2D2D2D"), LcdBorder = C("#2D2D2D"), LcdInk = "#FFFFFF",
        Glyph = C("#FFFFFF"), GlyphDisabled = C("#6E6E6E"),
        OrbMid = C("#FFFFFF"), OrbDark = C("#FFFFFF"), OrbHoverMid = C("#E8E8E8"), OrbHoverDark = C("#E8E8E8"),
        OrbRim = C("#2D2D2D"), OrbHeroRim = C("#FFFFFF"), OrbHoverRim = C("#3A3A3A"),
        SeekTop = C("#4A4A4A"), SeekBottom = C("#4A4A4A"), SeekLine = C("#4A4A4A"),
        FillLight = C("#60CDFF"), FillMid = C("#60CDFF"), FillDark = C("#60CDFF"),
        VolumeLight = C("#60CDFF"), VolumeDark = C("#60CDFF"),
        EdgeLight = C("#202020"), EdgeDark = C("#202020"),
    };

    /// <summary>Set for the PC versions of the retro device looks (PhoneSkin.Retro.Desktop.cs): their art and rail style.</summary>
    public DesktopRetroStyle? Retro { get; init; }

    public static IReadOnlyList<FullModeDesign> All { get; } = [Horizon, Luna, Fluent, .. PhoneSkin.DesktopRetroDesigns()];
}
