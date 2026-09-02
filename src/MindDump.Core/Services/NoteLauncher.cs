using System.Diagnostics;

namespace MindDump.Services;

public sealed class NoteLauncher
{
    public void Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No note exists for the current month yet.", path);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
