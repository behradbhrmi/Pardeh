using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Runtime.Scripting;

/// <summary>The <c>player</c> object skins script against (WMP 9 object model subset).</summary>
public sealed class PlayerObject : ScriptableObject
{
    public PlayerObject(SkinSession session)
    {
        var player = session.Player;
        var controls = new ControlsObject(player);
        var settings = new SettingsObject(player);
        var network = new NetworkObject();
        var dvd = new DvdObject();
        var playlist = new PlaylistObject(player);
        MediaObject? media = null;
        MediaItem? mediaSource = null;

        Property("status", () => player.Status);
        Property("playState", () => (double)player.PlayState);
        Property("openState", () => (double)player.OpenState);
        Property("URL", () => player.Url, v => player.Url = SkinValue.ToText(v));
        Property("controls", () => controls);
        Property("settings", () => settings);
        Property("network", () => network);
        Property("dvd", () => dvd);
        Property("currentPlaylist", () => playlist);
        Property("currentMedia", () =>
        {
            if (player.CurrentMedia is null)
                return null;
            if (!ReferenceEquals(mediaSource, player.CurrentMedia))
            {
                mediaSource = player.CurrentMedia;
                media = MediaObject.For(player, mediaSource);
            }

            return media;
        });
        Property("versionInfo", () => "9.0.0.0");
        Property("fullScreen", () => false);
        Property("uiMode", () => "full");
        Property("enabled", () => true);
        Property("enableContextMenu", () => true);
        Property("isOnline", () => true);
        Property("stretchToFit", () => false);
        Method("launchURL", args =>
        {
            session.RunModal(() => { session.Host.LaunchUrl(SkinValue.ToText(Arg(args, 0))); return true; });
            return null;
        });
        Method("close", () => session.RequestClose());
        Method("openPlayer", args =>
        {
            player.Url = SkinValue.ToText(Arg(args, 0));
            return null;
        });
    }
}

public sealed class ControlsObject : ScriptableObject
{
    public ControlsObject(MediaPlayer player)
    {
        Method("play", player.Play);
        Method("pause", player.Pause);
        Method("stop", player.Stop);
        Method("next", player.Next);
        Method("previous", player.Previous);
        Method("fastForward", player.FastForward);
        Method("fastReverse", player.FastReverse);
        Method("isAvailable", args => player.IsAvailable(SkinValue.ToText(Arg(args, 0))));
        Method("playItem", args =>
        {
            if (Arg(args, 0) is MediaObject media)
                player.PlayItem(player.CurrentPlaylist.Items.ToList().IndexOf(media.Item));
            return null;
        });
        Method("step", () => { });
        Method("seekBy", args =>
        {
            player.SeekBy(SkinValue.ToDouble(Arg(args, 0)));
            return null;
        });
        Property("currentPosition", () => player.CurrentPosition, v => player.CurrentPosition = SkinValue.ToDouble(v));
        Property("currentPositionString", () => player.CurrentPositionString);
        Property("currentPositionTimecode", () => player.CurrentPositionString);
        Property("currentItem", () => player.CurrentMedia is null ? null : MediaObject.For(player, player.CurrentMedia));
        Property("currentMarker", () => 0.0);
        Property("audioLanguageCount", () => 1.0);
    }
}

public sealed class SettingsObject : ScriptableObject
{
    public SettingsObject(MediaPlayer player)
    {
        var autoStart = true;
        Property("volume", () => player.Volume, v => player.Volume = SkinValue.ToDouble(v));
        Property("mute", () => player.Mute, v => player.Mute = SkinValue.ToBool(v));
        Property("balance", () => player.Balance, v => player.Balance = SkinValue.ToDouble(v));
        Property("autoStart", () => autoStart, v => autoStart = SkinValue.ToBool(v));
        Property("rate", () => player.Rate, v => player.Rate = SkinValue.ToDouble(v, 1));
        player.RateChanged += (_, _) => NotifyChanged("rate");
        Property("playCount", () => 1.0, _ => { });
        Property("enableErrorDialogs", () => false, _ => { });
        Property("invokeURLs", () => true, _ => { });
        Property("baseURL", () => string.Empty, _ => { });
        Method("getMode", args => SkinValue.ToText(Arg(args, 0)).ToLowerInvariant() switch
        {
            "shuffle" => player.Shuffle,
            "loop" => player.Loop,
            "autorewind" => true,
            _ => false,
        });
        Method("setMode", args =>
        {
            var value = SkinValue.ToBool(Arg(args, 1));
            switch (SkinValue.ToText(Arg(args, 0)).ToLowerInvariant())
            {
                case "shuffle": player.Shuffle = value; break;
                case "loop": player.Loop = value; break;
            }

            return null;
        });
        Method("isAvailable", _ => true);
    }
}

public sealed class MediaObject : ScriptableObject
{
    public MediaObject(MediaPlayer player, MediaItem item)
    {
        Item = item;
        Property("name", () => item.Name, v => item.Name = SkinValue.ToText(v));
        Property("sourceURL", () => item.Source);
        Property("duration", () => item.Duration.TotalSeconds > 0 ? item.Duration.TotalSeconds : player.Duration);
        Property("durationString", () => MediaPlayer.FormatTime(item.Duration.TotalSeconds > 0 ? item.Duration.TotalSeconds : player.Duration));
        Property("imageSourceWidth", () => (double)item.VideoWidth);
        Property("imageSourceHeight", () => (double)item.VideoHeight);
        Property("attributeCount", () => (double)item.Attributes.Count);
        Property("markerCount", () => 0.0);
        Method("getItemInfo", args => item.GetAttribute(SkinValue.ToText(Arg(args, 0))));
        Method("getItemInfoByAtom", _ => string.Empty);
        Method("getAttributeName", args =>
        {
            var index = (int)SkinValue.ToDouble(Arg(args, 0));
            return index >= 0 && index < item.Attributes.Count ? item.Attributes.Keys.ElementAt(index) : string.Empty;
        });
        Method("isReadOnlyItem", _ => true);
        Method("setItemInfo", args =>
        {
            item.Attributes[SkinValue.ToText(Arg(args, 0))] = SkinValue.ToText(Arg(args, 1));
            return null;
        });
        Method("isIdentical", args => Arg(args, 0) is MediaObject other && ReferenceEquals(other.Item, item));
    }

    public MediaItem Item { get; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MediaItem, MediaObject> Cache = new();

    /// <summary>One script object per media item, so scripts can compare them and proxies aren't recreated.</summary>
    public static MediaObject For(MediaPlayer player, MediaItem item) => Cache.GetValue(item, i => new MediaObject(player, i));
}

public sealed class PlaylistObject : ScriptableObject
{
    public PlaylistObject(MediaPlayer player)
    {
        var list = player.CurrentPlaylist;
        Property("name", () => list.Name, v => list.Name = SkinValue.ToText(v));
        Property("count", () => (double)list.Count);
        Property("attributeCount", () => 1.0);
        Method("item", args =>
        {
            var index = (int)SkinValue.ToDouble(Arg(args, 0), -1);
            return index >= 0 && index < list.Count ? MediaObject.For(player, list.Items[index]) : null;
        });
        Method("getItemInfo", args => SkinValue.ToText(Arg(args, 0)).ToLowerInvariant() switch
        {
            "name" or "title" => list.Name,
            _ => string.Empty,
        });
        Method("setItemInfo", () => { });
        Method("clear", list.Clear);
        Method("appendItem", args =>
        {
            if (Arg(args, 0) is MediaObject media)
                list.Add(media.Item);
            return null;
        });
    }
}

public sealed class NetworkObject : ScriptableObject
{
    public NetworkObject()
    {
        Property("bufferingProgress", () => 100.0);
        Property("downloadProgress", () => 100.0);
        Property("receptionQuality", () => 100.0);
        Property("bandWidth", () => 0.0);
        Property("bitRate", () => 0.0);
        Property("sourceProtocol", () => "File");
        Property("bufferingTime", () => 5000.0, _ => { });
        Property("lostPackets", () => 0.0);
        Property("receivedPackets", () => 0.0);
    }
}

public sealed class DvdObject : ScriptableObject
{
    public DvdObject()
    {
        Method("isAvailable", _ => false);
        Property("domain", () => string.Empty);
        Method("topMenu", () => { });
        Method("titleMenu", () => { });
        Method("back", () => { });
        Method("resume", () => { });
    }
}

/// <summary>The <c>mediacenter</c> object: global player UI state.</summary>
public sealed class MediaCenterObject : ScriptableObject
{
    /// <summary>The classic player's default UI state (colours measured from the 2003 "9 Series" look).</summary>
    private static readonly Dictionary<string, object?> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["contrastMode"] = string.Empty,
        ["appColorDark"] = "#212741",
        ["appColorMedium"] = "#394472",
        ["appColorLight"] = "#6071BF",
        ["appColorBlackBackground"] = "#000000",
        ["itemPlayingColor"] = "#00FF00",
        ["itemPlayingBackgroundColor"] = "#000000",
        ["itemErrorColor"] = "#FF0000",
        ["backHueShift"] = 0.0,
        ["backSaturation"] = 1.0,
        ["buttonHueShift"] = 0.0,
        ["buttonSaturation"] = 1.0,
        ["playlistWidth"] = 190.0,
        ["metadataWidth"] = 190.0,
        ["metadataHeight"] = 160.0,
        ["settingsHeight"] = 120.0,
        ["captionsHeight"] = 60.0,
        ["showPlaylist"] = true,
        ["showMetadata"] = true,
        ["showSettings"] = false,
        ["showCaptions"] = false,
        ["showTitles"] = true,
        ["showEffects"] = true,
        ["showResizeBars"] = true,
        ["showFullScreenPlaylist"] = false,
        ["displayView"] = "VizView",
        ["displayPreset"] = 0.0,
        ["metadataView"] = "MediaInfoView",
        ["metadataPreset"] = 0.0,
        ["settingsView"] = string.Empty,
        ["settingsPreset"] = 0.0,
        ["effectType"] = string.Empty,
        ["effectPreset"] = 0.0,
        ["vizRequest"] = string.Empty,
        ["vizAutoSelect"] = true,
        ["videoZoom"] = 0.0, // 0: fit the video to the pane; 50 / 100 / 200: that zoom (never larger than the pane)
        ["videoStretchToFit"] = false,
        ["userVideoStretchToFit"] = false,
        ["snapToVideo"] = true,
        ["quickHide"] = false,
        ["pinFullScreenControls"] = false,
        ["fullScreenAvailable"] = true,
        ["fullScreenRequest"] = false,
        ["dynamicRangeControl"] = 0.0,
        ["slowRate"] = 0.5,
        ["fastRate"] = 2.0,
    };

    /// <summary>The app colours the colour chooser recolours along with the chrome.</summary>
    private static readonly string[] ShiftedColors = ["appColorDark", "appColorMedium", "appColorLight"];

    public MediaCenterObject()
    {
        // Every value is settable and raises name_onchange for skins that listen.
        foreach (var (name, value) in Defaults)
            base.SetMember(name, value);
        Method("getNamedString", _ => string.Empty);
    }

    private static readonly string[] QuickHidePanels = ["showSettings", "showPlaylist", "showMetadata", "showTitles"];
    private Dictionary<string, object?>? _beforeQuickHide;

    /// <summary>True while "Maximize the Video and Visualization pane" has hidden the other panels.</summary>
    public bool QuickHidden => _beforeQuickHide is not null;

    public override void SetMember(string name, object? value)
    {
        if (name.Equals("quickHide", StringComparison.OrdinalIgnoreCase))
        {
            // Like the player: hide every panel but the video / visualization, and bring them back afterwards.
            var hide = SkinValue.ToBool(value);
            if (hide && _beforeQuickHide is null)
            {
                _beforeQuickHide = QuickHidePanels.ToDictionary(p => p, GetMember, StringComparer.OrdinalIgnoreCase);
                base.SetMember(name, true);
                foreach (var panel in QuickHidePanels)
                    base.SetMember(panel, false);
            }
            else if (!hide && _beforeQuickHide is { } saved)
            {
                base.SetMember(name, false);
                _beforeQuickHide = null;
                foreach (var (panel, before) in saved)
                    base.SetMember(panel, before);
            }

            return;
        }

        // Showing a panel by hand ends the quick hide.
        if (_beforeQuickHide is not null && QuickHidePanels.Contains(name, StringComparer.OrdinalIgnoreCase) && SkinValue.ToBool(value))
        {
            _beforeQuickHide = null;
            base.SetMember("quickHide", false);
        }

        base.SetMember(name, value);
        if (name.Equals("backHueShift", StringComparison.OrdinalIgnoreCase) || name.Equals("backSaturation", StringComparison.OrdinalIgnoreCase))
        {
            // Like the classic player: the panel colours follow the chrome's hue and saturation.
            var hue = SkinValue.ToDouble(GetMember("backHueShift"));
            var saturation = SkinValue.ToDouble(GetMember("backSaturation"), 1);
            foreach (var color in ShiftedColors)
            {
                if (SkinValue.ParseColor(Defaults[color]) is { } rgb)
                    base.SetMember(color, ToHex(Imaging.SkinBitmap.ShiftColor(rgb, hue, saturation)));
            }
        }
    }

    private static string ToHex(int rgb) => "#" + rgb.ToString("X6", System.Globalization.CultureInfo.InvariantCulture);
}
