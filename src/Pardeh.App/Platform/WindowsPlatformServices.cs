using System.Diagnostics;
using Avalonia.Controls;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;
using Pardeh.UI;

namespace Pardeh.App.Platform;

/// <summary>The Windows desktop head's answers to <see cref="IPlatformServices"/>: Win32, SMTC, taskbar, registry.</summary>
internal sealed class WindowsPlatformServices : IPlatformServices
{
    private static IntPtr Handle(TopLevel? top) => top?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public IDisposable? StartSystemIntegration(TopLevel top, MediaPlayer player, Action<string> log)
    {
        var hwnd = Handle(top);
        var mediaControls = MediaControls.TryCreate(hwnd, player, log);
        if (top is Window window)
            _ = TaskbarButtons.TryCreate(window, hwnd, player, log); // kept alive by its window hook
        try { Pardeh.App.Platform.FileAssociations.EnsureShellAppearance(); } catch (Exception) { }
        var widget = MiniWidget.Attach(player);
        return new Composite(mediaControls, widget);
    }

    public void ListenForOpenRequests(Action<string[]> received) => SingleInstance.Listen(received);

    public string? OpenMediaFile(TopLevel? owner) => NativeDialogs.OpenMediaFile(Handle(owner));

    public bool Confirm(TopLevel? owner, string text, string caption) => NativeDialogs.Confirm(Handle(owner), text, caption);

    public void Inform(TopLevel? owner, string text, string caption) => NativeDialogs.Inform(Handle(owner), text, caption);

    public void SetRoundedCorners(TopLevel? window, bool rounded) => WindowCorners.Set(Handle(window), rounded);

    public void ApplyWindowShape(TopLevel? window, SkinMask mask, double pixelScale) => WindowShape.Apply(Handle(window), mask, pixelScale);

    public void ClearWindowShape(TopLevel? window) => WindowShape.Clear(Handle(window));

    public void RevealInFileManager(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    public void OpenFolder(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public IFileAssociationService? FileAssociations { get; } = new WindowsFileAssociations();

    public IWmpImportService? WmpImport { get; } = new WindowsWmpImport();

    private sealed class WindowsFileAssociations : IFileAssociationService
    {
        public IReadOnlyList<(string Group, string[] Extensions)> Groups => Platform.FileAssociations.Groups;

        public bool IsRegistered() => Platform.FileAssociations.IsRegistered();

        public void Register(IEnumerable<string> extensions) => Platform.FileAssociations.Register(extensions);

        public void Unregister() => Platform.FileAssociations.Unregister();

        public void OpenDefaultAppsSettings() => Platform.FileAssociations.OpenDefaultAppsSettings();
    }

    private sealed class WindowsWmpImport : IWmpImportService
    {
        public string TargetDirectory => WmpImporter.TargetDirectory;

        public string Import(string source, Action<string> progress) => WmpImporter.Import(source, progress);
    }
}

/// <summary>Disposes several parts together.</summary>
internal sealed class Composite(params IDisposable?[] parts) : IDisposable
{
    public void Dispose()
    {
        foreach (var part in parts)
            part?.Dispose();
    }
}
