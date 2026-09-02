# MindDump

MindDump is a Windows tray utility for capturing thoughts into monthly Markdown files under `Calendar\Monthly` in an Obsidian vault.

## Behavior

- Launch the app and select the Obsidian vault root on first run.
- Press `Ctrl+Shift+Space` from anywhere to open the capture popup.
- In quick capture mode, press `Enter` to save or `Shift+Enter` for another line.
- Switch to writing mode in Settings to use `Ctrl+Enter` to save.
- Each entry is appended to `Calendar\Monthly\yyyy-MM Mind Dump.md` under a daily heading.
- Undo the most recent capture from the capture window or tray menu. Undo history lasts for the current app session.

## Build

The project targets `.NET 10` and WinUI 3 through Windows App SDK `2.1.3`.

```powershell
dotnet build MindDump.slnx -c Debug
dotnet test --solution MindDump.slnx -c Debug
dotnet publish src\MindDump\MindDump.csproj -c Release -p:PublishProfile=win-x64
```

The release publish output is an unpackaged, self-contained `win-x64` application.
