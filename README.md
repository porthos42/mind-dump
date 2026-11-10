# MindDump

MindDump is a Windows tray utility for capturing thoughts into monthly Markdown files under `Calendar\Monthly` in an Obsidian vault.

## Behavior

- Launch the app and select the Obsidian vault root on first run.
- Press `Ctrl+Shift+Space` from anywhere to open the capture popup.
- In quick capture mode, press `Enter` to save or `Shift+Enter` for another line.
- Switch to writing mode in Settings to use `Ctrl+Enter` to save.
- Each entry is appended to `Calendar\Monthly\yyyy-MM Mind Dump.md` under a daily heading.
- Review the 10 most recent tagged captures, edit or remove one, copy it back into the capture box, or open its monthly note.
- New entries include stable identifiers in Obsidian-native `%%` comments, hidden in Reading View and Live Preview. Older MindDump HTML markers are migrated automatically.
- Undo the most recent capture from the capture window or tray menu. The tray undo remains limited to the current app session.

## Build

The project targets `.NET 10` and WinUI 3 through Windows App SDK `2.1.3`.

```powershell
dotnet build MindDump.slnx -c Debug
dotnet test --solution MindDump.slnx -c Debug
dotnet publish src\MindDump\MindDump.csproj -c Release -p:PublishProfile=win-x64
```

The release publish output is an unpackaged, self-contained `win-x64` application.

For the per-user Windows installer, repeatable build command, and upgrade/uninstall
test checklist, see [installer/README.md](installer/README.md).
