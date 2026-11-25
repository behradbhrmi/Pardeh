using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Pardeh.Widgets;

/// <summary>
/// The Windows 11 Widgets board provider of Pardeh: an out-of-process COM server that the Widgets board starts when a Pardeh widget
/// is pinned (the package manifest registers the class). It shows what the running Pardeh player plays and sends it the card's buttons.
/// </summary>
internal static class Program
{
    /// <summary>The provider's COM class; the same GUID is in the package manifest (com:Class Id and CreateInstance ClassId).</summary>
    public static readonly Guid ClassId = new("6F1B7C3A-5D2E-4B7A-9C41-2E8D5A3F7B10");

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid rclsid, [MarshalAs(UnmanagedType.IUnknown)] object pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [STAThread]
    private static void Main()
    {
        ComWrappersSupport.InitializeComWrappers();
        var clsid = ClassId;
        var factory = new WidgetProviderFactory();
        var hr = CoRegisterClassObject(ref clsid, factory, 0x4 /* CLSCTX_LOCAL_SERVER */, 0x1 /* REGCLS_MULTIPLEUSE */, out var cookie);
        if (hr != 0)
            return;
        try
        {
            // Lives while widgets are pinned; the provider releases this when the last one is removed or the platform ends the session.
            WidgetProvider.Exit.WaitOne();
        }
        finally
        {
            CoRevokeClassObject(cookie);
        }
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer(bool fLock);
    }

    [ComVisible(true)]
    private sealed class WidgetProviderFactory : IClassFactory
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero)
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            var unknown = MarshalInspectable<IWidgetProvider>.FromManaged(new WidgetProvider());
            try
            {
                return Marshal.QueryInterface(unknown, ref riid, out ppvObject);
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        public int LockServer(bool fLock) => 0;
    }
}
