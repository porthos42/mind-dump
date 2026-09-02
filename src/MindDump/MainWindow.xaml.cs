using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MindDump.Interop;
using MindDump.Models;
using MindDump.Services;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace MindDump;

public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly CaptureWriter _writer;
    private readonly NoteLauncher _noteLauncher = new();
    private readonly NativeMessageHost _messageHost;
    private readonly TrayService _trayService;
    private readonly HotkeyService _hotkeyService;
    private readonly AppWindow _appWindow;
    private AppSettings _settings = new();
    private CaptureUndoToken? _lastCapture;
    private string? _lastCaptureText;
    private bool _isExiting;

    public MainWindow(SettingsService settingsService, CaptureWriter writer)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _writer = writer;

        nint windowHandle = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle));
        _appWindow.Resize(new Windows.Graphics.SizeInt32(720, 560));
        _appWindow.Title = "MindDump";
        _appWindow.Closing += AppWindow_Closing;

        _messageHost = new NativeMessageHost(windowHandle);
        _messageHost.HotkeyPressed += ShowCapture;
        _messageHost.TrayCommandReceived += HandleTrayCommand;
        _hotkeyService = new HotkeyService(windowHandle);
        _trayService = new TrayService(windowHandle);
        _trayService.Add();
    }

    public async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsService.LoadAsync();
            UpdateModeText();
            ApplyHotkey();

            if (string.IsNullOrWhiteSpace(_settings.VaultFolderPath))
            {
                ShowCapture();
                StatusText.Text = "Select your Obsidian vault. Mind dumps will be stored in Calendar\\Monthly.";
                await ShowSettingsAsync(requireFolder: true);
                return;
            }

            HideCapture();
        }
        catch (Exception exception)
        {
            ShowCapture();
            StatusText.Text = $"Could not initialize MindDump: {exception.Message}";
        }
    }

    private void ShowCapture()
    {
        Activate();
        _ = RefreshRecentCapturesAsync();
        CaptureTextBox.Focus(FocusState.Programmatic);
    }

    private void HideCapture()
    {
        _appWindow.Hide();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await SaveCaptureAsync();
    }

    private async void CaptureTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        bool controlDown = IsKeyDown(VirtualKey.Control);
        bool shiftDown = IsKeyDown(VirtualKey.Shift);
        bool shouldSubmit = _settings.InputMode switch
        {
            InputMode.QuickCapture => !shiftDown,
            InputMode.Writing => controlDown,
            _ => false
        };

        if (!shouldSubmit)
        {
            return;
        }

        e.Handled = true;
        await SaveCaptureAsync();
    }

    private async Task SaveCaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(CaptureTextBox.Text))
        {
            StatusText.Text = "Type a thought before capturing it.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.VaultFolderPath))
        {
            StatusText.Text = "Choose your Obsidian vault in Settings first.";
            await ShowSettingsAsync(requireFolder: true);
            return;
        }

        try
        {
            string capturedText = CaptureTextBox.Text;
            _lastCapture = await _writer.AppendWithUndoAsync(_settings.VaultFolderPath, capturedText, DateTimeOffset.Now);
            _lastCaptureText = capturedText;
            UndoButton.IsEnabled = true;
            await RefreshRecentCapturesAsync();
            CaptureTextBox.Text = string.Empty;
            StatusText.Text = string.Empty;
            HideCapture();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not capture this thought: {exception.Message}";
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync(requireFolder: false);
    }

    private async Task ShowSettingsAsync(bool requireFolder)
    {
        AppSettings edited = CloneSettings(_settings);
        TextBlock folderPathText = new()
        {
            Text = GetMonthlyLocationText(edited.VaultFolderPath),
            TextWrapping = TextWrapping.Wrap
        };
        Button chooseFolderButton = new() { Content = "Choose vault" };
        chooseFolderButton.Click += async (_, _) =>
        {
            string? path = await PickFolderAsync();
            if (path is not null)
            {
                edited.VaultFolderPath = path;
                folderPathText.Text = GetMonthlyLocationText(path);
            }
        };

        ComboBox modeBox = new()
        {
            ItemsSource = new[] { "Quick capture", "Writing mode" },
            SelectedIndex = edited.InputMode == InputMode.QuickCapture ? 0 : 1
        };
        CheckBox controlBox = new() { Content = "Ctrl", IsChecked = edited.Hotkey.Control };
        CheckBox shiftBox = new() { Content = "Shift", IsChecked = edited.Hotkey.Shift };
        CheckBox altBox = new() { Content = "Alt", IsChecked = edited.Hotkey.Alt };
        CheckBox windowsBox = new() { Content = "Win", IsChecked = edited.Hotkey.Windows };
        ComboBox keyBox = new()
        {
            ItemsSource = new[] { "Space" }.Concat(Enumerable.Range('A', 26).Select(value => ((char)value).ToString())).ToArray(),
            SelectedItem = edited.Hotkey.Key
        };

        StackPanel hotkeyOptions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeyOptions.Children.Add(controlBox);
        hotkeyOptions.Children.Add(shiftBox);
        hotkeyOptions.Children.Add(altBox);
        hotkeyOptions.Children.Add(windowsBox);
        hotkeyOptions.Children.Add(keyBox);

        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "Obsidian vault root", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(folderPathText);
        content.Children.Add(chooseFolderButton);
        content.Children.Add(new TextBlock
        {
            Text = @"Mind dumps are saved in Calendar\Monthly inside the selected vault.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        content.Children.Add(new TextBlock { Text = "Input mode", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(modeBox);
        content.Children.Add(new TextBlock { Text = "Global shortcut", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(hotkeyOptions);

        ContentDialog dialog = new()
        {
            Title = "MindDump settings",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = requireFolder ? "Later" : "Cancel",
            XamlRoot = Content.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(edited.VaultFolderPath))
        {
            StatusText.Text = "Select your Obsidian vault before saving settings.";
            return;
        }

        edited.InputMode = modeBox.SelectedIndex == 0 ? InputMode.QuickCapture : InputMode.Writing;
        edited.Hotkey.Control = controlBox.IsChecked is true;
        edited.Hotkey.Shift = shiftBox.IsChecked is true;
        edited.Hotkey.Alt = altBox.IsChecked is true;
        edited.Hotkey.Windows = windowsBox.IsChecked is true;
        edited.Hotkey.Key = (string?)keyBox.SelectedItem ?? "Space";

        _settings = edited;
        await _settingsService.SaveAsync(_settings);
        UpdateModeText();
        ApplyHotkey();
        StatusText.Text = "Settings saved.";
        await RefreshRecentCapturesAsync();
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    private void ApplyHotkey()
    {
        if (_hotkeyService.Register(_settings.Hotkey))
        {
            StatusText.Text = string.Empty;
        }
        else
        {
            StatusText.Text = $"The shortcut {_settings.Hotkey.DisplayText} is already in use. Choose another shortcut in Settings.";
        }
    }

    private async void HandleTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Capture:
                ShowCapture();
                break;
            case TrayCommand.OpenCurrentNote:
                OpenCurrentNote();
                break;
            case TrayCommand.UndoLastCapture:
                await UndoLastCaptureAsync();
                break;
            case TrayCommand.Settings:
                ShowCapture();
                await ShowSettingsAsync(requireFolder: false);
                break;
            case TrayCommand.Exit:
                ExitApplication();
                break;
        }
    }

    private void OpenCurrentNote()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_settings.VaultFolderPath))
            {
                throw new InvalidOperationException("Choose your Obsidian vault in Settings first.");
            }

            _noteLauncher.Open(_writer.GetMonthlyFilePath(_settings.VaultFolderPath, DateTimeOffset.Now));
        }
        catch (Exception exception)
        {
            ShowCapture();
            StatusText.Text = exception.Message;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        args.Cancel = true;
        HideCapture();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        HideCapture();
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        await UndoLastCaptureAsync();
    }

    private async Task UndoLastCaptureAsync()
    {
        if (_lastCapture is null)
        {
            ShowCapture();
            StatusText.Text = "There is no capture to undo in this session.";
            return;
        }

        try
        {
            await _writer.UndoAsync(_lastCapture);
            CaptureTextBox.Text = _lastCaptureText ?? string.Empty;
            CaptureTextBox.SelectionStart = CaptureTextBox.Text.Length;
            _lastCapture = null;
            _lastCaptureText = null;
            UndoButton.IsEnabled = false;
            ShowCapture();
            StatusText.Text = "Last capture undone. The text is ready to edit or capture again.";
            await RefreshRecentCapturesAsync();
        }
        catch (Exception exception)
        {
            ShowCapture();
            StatusText.Text = $"Could not undo the last capture: {exception.Message}";
        }
    }

    private async Task RefreshRecentCapturesAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.VaultFolderPath))
        {
            RecentCapturesList.ItemsSource = null;
            return;
        }

        try
        {
            await _writer.MigrateLegacyMarkersAsync(_settings.VaultFolderPath);
            IReadOnlyList<CaptureEntry> entries = await _writer.GetRecentAsync(_settings.VaultFolderPath);
            RecentCapturesList.ItemsSource = entries.Select(entry => new RecentCaptureItem(entry)).ToArray();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not load recent captures: {exception.Message}";
        }
    }

    private void UseRecentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CaptureEntry entry)
        {
            return;
        }

        CaptureTextBox.Text = entry.Text;
        CaptureTextBox.SelectionStart = CaptureTextBox.Text.Length;
        CaptureTextBox.Focus(FocusState.Programmatic);
        StatusText.Text = "Copied to the capture box.";
    }

    private async void EditRecentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CaptureEntry entry)
        {
            return;
        }

        TextBox editor = new() { Text = entry.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120 };
        ContentDialog dialog = new()
        {
            Title = "Edit capture",
            Content = editor,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _writer.UpdateEntryAsync(entry, editor.Text);
            InvalidateSessionUndoFor(entry.FilePath);
            await RefreshRecentCapturesAsync();
            StatusText.Text = "Capture updated.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not update the capture: {exception.Message}";
        }
    }

    private async void DeleteRecentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CaptureEntry entry)
        {
            return;
        }

        try
        {
            await _writer.DeleteEntryAsync(entry);
            InvalidateSessionUndoFor(entry.FilePath);
            await RefreshRecentCapturesAsync();
            StatusText.Text = "Capture undone.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not undo the capture: {exception.Message}";
        }
    }

    private void OpenRecentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CaptureEntry entry)
        {
            return;
        }

        try
        {
            _noteLauncher.Open(entry.FilePath);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Could not open the note: {exception.Message}";
        }
    }

    private void InvalidateSessionUndoFor(string filePath)
    {
        if (_lastCapture is null || !string.Equals(_lastCapture.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastCapture = null;
        _lastCaptureText = null;
        UndoButton.IsEnabled = false;
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _hotkeyService.Dispose();
        _trayService.Dispose();
        _messageHost.Dispose();
        Close();
    }

    private void UpdateModeText()
    {
        ModeText.Text = _settings.InputMode == InputMode.QuickCapture
            ? "Enter to save, Shift+Enter for a new line"
            : "Ctrl+Enter to save, Enter for a new line";
    }

    private static bool IsKeyDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static AppSettings CloneSettings(AppSettings source)
        => new()
        {
            VaultFolderPath = source.VaultFolderPath,
            InputMode = source.InputMode,
            Hotkey = new HotkeySettings
            {
                Control = source.Hotkey.Control,
                Shift = source.Hotkey.Shift,
                Alt = source.Hotkey.Alt,
                Windows = source.Hotkey.Windows,
                Key = source.Hotkey.Key
            }
        };

    private string GetMonthlyLocationText(string? vaultFolderPath)
        => string.IsNullOrWhiteSpace(vaultFolderPath)
            ? "No vault selected"
            : _writer.GetMonthlyFolderPath(vaultFolderPath);
}

public sealed class RecentCaptureItem(CaptureEntry entry)
{
    public CaptureEntry Entry { get; } = entry;
    public string When { get; } = entry.CapturedAt.ToString("MMM d, HH:mm");
    public string Preview { get; } = entry.Text.Replace('\n', ' ');
}
