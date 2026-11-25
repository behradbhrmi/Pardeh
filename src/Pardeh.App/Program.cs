using Avalonia;

using Pardeh.UI;

namespace Pardeh.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Pardeh.exe --import-xp <ISO | I386 folder | WMPLOC.DL_ | wmploc.dll>: the menu's import, without the UI.
        if (args.Length == 2 && args[0].Equals("--import-xp", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var summary = Platform.WmpImporter.Import(args[1], _ => { });
                File.WriteAllText(Path.Combine(Platform.WmpImporter.TargetDirectory, "import.txt"), $"{DateTime.Now:u} {args[1]}: {summary}");
                return 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        // Already running: hand the files to that window and leave.
        if (!Platform.SingleInstance.Claim(args))
            return 0;

        PlatformServices.Current = new Platform.WindowsPlatformServices();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PardehApplication>()
            .UsePlatformDetect()
            .LogToTrace();
}
