using Avalonia;
using Avalonia.Headless;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The headless Skia platform that every test which draws needs. There is one lock for the whole run: test classes run in
/// parallel, and a second setup attempt fails at once even while the first is still under way, so a class with a lock of its
/// own could start drawing before the platform was ready.
/// </summary>
internal static class TestPlatform
{
    private static readonly object Gate = new();
    private static bool _up;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_up)
                return;
            AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            _up = true;
        }
    }
}
