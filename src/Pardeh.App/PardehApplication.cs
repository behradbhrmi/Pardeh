using Avalonia.Controls.ApplicationLifetimes;
using Pardeh.UI;

namespace Pardeh.App;

public sealed class PardehApplication : PardehApplicationBase
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startup = StartupOptions.Parse(desktop.Args ?? []);
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new SkinWindow(startup);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
