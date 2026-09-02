using System.Runtime.InteropServices;
using MindDump.Models;

namespace MindDump.Interop;

public sealed class HotkeyService(nint windowHandle) : IDisposable
{
    private const int HotkeyId = 0x4D44;
    private bool _registered;

    public bool Register(HotkeySettings settings)
    {
        Unregister();
        uint modifiers = 0;
        modifiers |= settings.Alt ? NativeMethods.ModAlt : 0;
        modifiers |= settings.Control ? NativeMethods.ModControl : 0;
        modifiers |= settings.Shift ? NativeMethods.ModShift : 0;
        modifiers |= settings.Windows ? NativeMethods.ModWin : 0;
        uint key = string.Equals(settings.Key, "Space", StringComparison.OrdinalIgnoreCase)
            ? NativeMethods.VirtualKeySpace
            : char.ToUpperInvariant(settings.Key[0]);

        _registered = NativeMethods.RegisterHotKey(windowHandle, HotkeyId, modifiers | NativeMethods.ModNoRepeat, key);
        return _registered;
    }

    public void Dispose()
    {
        Unregister();
    }

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(windowHandle, HotkeyId);
        _registered = false;
    }
}

internal static partial class NativeMethods
{
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VirtualKeySpace = 0x20;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);
}
