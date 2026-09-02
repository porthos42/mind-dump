using System.Runtime.InteropServices;

namespace MindDump.Interop;

public sealed class NativeMessageHost : IDisposable
{
    internal const uint TrayCallbackMessage = 0x8000 + 1;
    private const uint WindowMessageHotkey = 0x0312;
    private const uint WindowMessageCommand = 0x0111;
    private const uint WindowMessageContextMenu = 0x007B;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const int WindowProcedureIndex = -4;
    private const uint MenuString = 0x0000;
    private const uint MenuSeparator = 0x0800;
    private const uint TrackMenuRightButton = 0x0002;

    private readonly nint _windowHandle;
    private readonly WindowProcedure _procedure;
    private readonly nint _previousProcedure;

    public NativeMessageHost(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _procedure = ProcessMessage;
        _previousProcedure = SetWindowLongPtr(_windowHandle, WindowProcedureIndex, Marshal.GetFunctionPointerForDelegate(_procedure));
    }

    public event Action? HotkeyPressed;

    public event Action<TrayCommand>? TrayCommandReceived;

    public void Dispose()
    {
        SetWindowLongPtr(_windowHandle, WindowProcedureIndex, _previousProcedure);
        GC.KeepAlive(_procedure);
    }

    private nint ProcessMessage(nint windowHandle, uint message, nuint wParam, nint lParam)
    {
        if (message == WindowMessageHotkey)
        {
            HotkeyPressed?.Invoke();
            return 0;
        }

        if (message == TrayCallbackMessage)
        {
            uint trayEvent = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (trayEvent == WindowMessageContextMenu)
            {
                ShowMenu();
            }
            else if (trayEvent == WindowMessageLeftButtonUp)
            {
                TrayCommandReceived?.Invoke(TrayCommand.Capture);
            }

            return 0;
        }

        if (message == WindowMessageCommand)
        {
            int command = unchecked((int)wParam) & 0xFFFF;
            if (Enum.IsDefined(typeof(TrayCommand), command))
            {
                TrayCommandReceived?.Invoke((TrayCommand)command);
                return 0;
            }
        }

        return CallWindowProc(_previousProcedure, windowHandle, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        AppendMenu(menu, MenuString, (nuint)TrayCommand.Capture, "Capture");
        AppendMenu(menu, MenuString, (nuint)TrayCommand.OpenCurrentNote, "Open Current Note");
        AppendMenu(menu, MenuString, (nuint)TrayCommand.Settings, "Settings");
        AppendMenu(menu, MenuSeparator, 0, null);
        AppendMenu(menu, MenuString, (nuint)TrayCommand.Exit, "Exit");
        GetCursorPos(out Point point);
        SetForegroundWindow(_windowHandle);
        TrackPopupMenu(menu, TrackMenuRightButton, point.X, point.Y, 0, _windowHandle, 0);
        DestroyMenu(menu);
    }

    private delegate nint WindowProcedure(nint windowHandle, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newProcedure);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint previousProcedure, nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint itemId, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint windowHandle, nint rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);
}
