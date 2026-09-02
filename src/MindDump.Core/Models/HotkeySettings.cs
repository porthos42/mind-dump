namespace MindDump.Models;

public sealed class HotkeySettings
{
    public bool Control { get; set; } = true;

    public bool Shift { get; set; } = true;

    public bool Alt { get; set; }

    public bool Windows { get; set; }

    public string Key { get; set; } = "Space";

    public string DisplayText
    {
        get
        {
            List<string> parts = [];
            if (Control)
            {
                parts.Add("Ctrl");
            }

            if (Shift)
            {
                parts.Add("Shift");
            }

            if (Alt)
            {
                parts.Add("Alt");
            }

            if (Windows)
            {
                parts.Add("Win");
            }

            parts.Add(Key);
            return string.Join("+", parts);
        }
    }
}
