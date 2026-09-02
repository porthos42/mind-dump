using MindDump.Models;
using MindDump.Services;

namespace MindDump.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"MindDumpSettingsTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsExpectedDefaults()
    {
        SettingsService service = CreateService();

        AppSettings settings = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Null(settings.VaultFolderPath);
        Assert.Equal(InputMode.QuickCapture, settings.InputMode);
        Assert.Equal("Ctrl+Shift+Space", settings.Hotkey.DisplayText);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsPersistedSettings()
    {
        SettingsService service = CreateService();
        AppSettings expected = new()
        {
            VaultFolderPath = @"C:\Vault\Inbox",
            InputMode = InputMode.Writing,
            Hotkey = new HotkeySettings
            {
                Control = true,
                Shift = false,
                Alt = true,
                Key = "M"
            }
        };

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(expected, cancellationToken);
        AppSettings actual = await service.LoadAsync(cancellationToken);

        Assert.Equal(expected.VaultFolderPath, actual.VaultFolderPath);
        Assert.Equal(InputMode.Writing, actual.InputMode);
        Assert.Equal("Ctrl+Alt+M", actual.Hotkey.DisplayText);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SettingsService CreateService()
        => new(Path.Combine(_directory, "settings.json"));
}
