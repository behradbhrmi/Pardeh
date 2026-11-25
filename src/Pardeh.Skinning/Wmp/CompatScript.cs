namespace Pardeh.Skinning.Wmp;

/// <summary>
/// Pardeh's stand-in for the shared script classic skins load from the player's own resources
/// (res://wmploc.dll/RT_TEXT/#132). Original code: it only defines functions that the skin didn't
/// define itself, so a skin's own versions always win.
/// </summary>
internal static class CompatScript
{
    public const string Source = """
        (function (g) {
            function def(name, fn) { if (typeof g[name] !== 'function') g[name] = fn; }
            function showStatus(text) {
                if (typeof metadata !== 'undefined' && metadata) { metadata.value = text; }
            }

            def('OnTimerTransport', function () { });
            def('OnStatusChangeTransport', function () { showStatus(player.status); });
            def('OnOpenStateChangeTransport', function (state) { showStatus(player.status); });
            def('OnPlayStateChangeTransport', function (state) { showStatus(player.status); });
            def('OnBufferingChangeTransport', function () { });
            def('UpdateReceptionQuality', function () { });
            def('OnDisconnectTransport', function () { showStatus('Disconnected'); });
            def('InitControls', function () { });
            def('InitMiniControls', function () { });
        })(this);
        """;
}
