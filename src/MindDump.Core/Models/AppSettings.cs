namespace MindDump.Models;

public sealed class AppSettings
{
    public string? VaultFolderPath { get; set; }

    public HotkeySettings Hotkey { get; set; } = new();

    public InputMode InputMode { get; set; } = InputMode.QuickCapture;
}
