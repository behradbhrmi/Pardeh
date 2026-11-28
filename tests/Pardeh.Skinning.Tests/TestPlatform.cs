using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Headless;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The headless Skia platform that every test which draws needs. There is one lock for the whole run: test classes run in
/// parallel, and a second setup attempt fails at once even while the first is still under way, so a class with a lock of its
/// own could start drawing before the platform was ready.
/// </summary>
/// <remarks>
/// Avalonia is started on a thread of its own, because a window works only on the thread that started it: a test that shows
/// one runs there (<see cref="OnUiThread"/>). No message loop runs, so posted work waits until such a test asks for it.
/// </remarks>
internal static class TestPlatform
{
    private static readonly object Gate = new();
    private static readonly BlockingCollection<Action> Work = [];
    private static bool _up;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_up)
                return;
            using var ready = new ManualResetEventSlim();
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                ready.Set();
                foreach (var work in Work.GetConsumingEnumerable())
                    work();
            })
            { IsBackground = true, Name = "Avalonia tests" };
            thread.Start();
            ready.Wait();
            if (failure is not null)
                ExceptionDispatchInfo.Throw(failure);
            _up = true;
        }
    }

    /// <summary>Runs a test body on the thread Avalonia was started on, and waits for it.</summary>
    public static void OnUiThread(Action body)
    {
        Ensure();
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        Work.Add(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            done.Set();
        });
        done.Wait();
        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
