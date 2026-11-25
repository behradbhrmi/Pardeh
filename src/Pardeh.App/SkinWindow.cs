using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Pardeh.UI;

namespace Pardeh.App;

/// <summary>
/// The desktop shell: a borderless, transparent window around the shared <see cref="PlayerView"/>, which shapes it
/// to the current skin (through the Windows platform services) and does everything else.
/// </summary>
internal sealed class SkinWindow : Window
{
    private readonly PlayerView _view;

    public SkinWindow(StartupOptions startup)
    {
        Title = "Pardeh";
        try
        {
            // The taskbar / Alt+Tab icon: the Pardeh logo (also the exe icon, see ApplicationIcon in the project file).
            if (typeof(SkinWindow).Assembly.GetManifestResourceStream("pardeh.ico") is { } icon)
                Icon = new WindowIcon(icon);
        }
        catch (Exception)
        {
            // Whatever the platform's icon loader throws (Win32, decoding, I/O): an icon is not worth failing the window for.
        }

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.Manual;
        Width = 400;
        Height = 300;
        _view = new PlayerView(startup) { MiniWidgetToggle = MiniWidget.Toggle }; // the desktop widget (mini player)
        Content = _view;
        Opened += (_, _) => _view.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _view.Shutdown();
        base.OnClosed(e);
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        _view.OnShellResized(e.Reason == WindowResizeReason.User);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _view.HandleKeyDown(e);
    }
}
