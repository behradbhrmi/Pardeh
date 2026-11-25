using Avalonia.Controls;
using Pardeh.UI;

namespace Pardeh.Android;

internal static class AndroidHost
{
    /// <summary>
    /// The shared player view as the app's only view. It starts once attached to the top level (the desktop head
    /// starts it after its window opened) and gets the top level's key presses (hardware keyboards, media keys).
    /// </summary>
    public static Control CreateMainView()
    {
        // TODO(android): files to open come from VIEW intents / the document picker, not the command line.
        var view = new PlayerView(StartupOptions.Parse([]));
        view.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(view) is { } top)
            {
                top.KeyDown += (_, key) => view.HandleKeyDown(key);
                // Back: close a menu or panel, then step back to Now Playing, then go to the background (never kill).
                top.BackRequested += (_, back) =>
                {
                    if (!view.HandleBack())
                        PlatformServices.Current.MoveToBackground();
                    back.Handled = true;
                };
            }
            view.Start();
        };
        return view;
    }
}
