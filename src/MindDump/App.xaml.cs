using Microsoft.UI.Xaml;
using MindDump.Services;

namespace MindDump;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settingsService = new SettingsService();
        var writer = new CaptureWriter();
        _window = new MainWindow(settingsService, writer);
        _window.Activate();
        _ = _window.InitializeAsync();
    }
}
