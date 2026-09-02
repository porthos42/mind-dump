using System.Runtime.InteropServices;

namespace MindDump.Interop;

public enum TrayCommand
{
    Capture = 1001,
    OpenCurrentNote = 1002,
    Settings = 1003,
    Exit = 1004,
    UndoLastCapture = 1005
}

public sealed class TrayService(nint windowHandle) : IDisposable
{
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint IconMessage = 0x00000001;
    private const uint IconIcon = 0x00000002;
    private const uint IconTip = 0x00000004;
    private const uint IconId = 1;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint LoadDefaultSize = 0x0040;
    private bool _added;
    private nint _iconHandle;
    private bool _ownsIconHandle;

    public void Add()
    {
        _iconHandle = LoadTrayIcon();
        NotifyIconData data = CreateData();
        if (!ShellNotifyIcon(NotifyIconAdd, ref data))
        {
            ReleaseIcon();
            return;
        }

        data.VersionOrTimeout = NotifyIconVersion4;
        ShellNotifyIcon(NotifyIconSetVersion, ref data);
        _added = true;
    }

    public void Dispose()
    {
        if (!_added)
        {
            return;
        }

        NotifyIconData data = CreateData();
        ShellNotifyIcon(NotifyIconDelete, ref data);
        _added = false;
        ReleaseIcon();
    }

    private NotifyIconData CreateData()
        => new()
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = windowHandle,
            Id = IconId,
            Flags = IconMessage | IconIcon | IconTip,
            CallbackMessage = NativeMessageHost.TrayCallbackMessage,
            IconHandle = _iconHandle,
            Tip = "MindDump"
        };

    private nint LoadTrayIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MindDump.ico");
        nint iconHandle = LoadImage(0, iconPath, ImageIcon, 0, 0, LoadFromFile | LoadDefaultSize);
        if (iconHandle != 0)
        {
            _ownsIconHandle = true;
            return iconHandle;
        }

        _ownsIconHandle = false;
        return LoadIcon(0, (nint)32512);
    }

    private void ReleaseIcon()
    {
        if (_ownsIconHandle && _iconHandle != 0)
        {
            DestroyIcon(_iconHandle);
        }

        _iconHandle = 0;
        _ownsIconHandle = false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? Info;

        public uint VersionOrTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    private static bool ShellNotifyIcon(uint message, ref NotifyIconData data)
        => Shell_NotifyIcon(message, ref data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
