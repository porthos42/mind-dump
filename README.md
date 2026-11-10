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

## Download and update

Download Windows x64 assets from [GitHub Releases](https://github.com/porthos42/mind-dump/releases).
Use Setup.exe for a per-user installation, or extract the entire portable ZIP and
run MindDump.exe. Windows 10 build 19041 or later is required; no separate .NET
installation is needed. The app starts in the system tray.

Downloads are currently unsigned. Windows may display an unknown publisher or
SmartScreen warning. Verify the release origin; do not disable Windows protections
globally. When provided, compare SHA256SUMS.txt with
`Get-FileHash <download> -Algorithm SHA256` to detect download corruption.

Updates are manual: save/copy unsaved input, choose tray **Exit**, then run the
new installer. For portable updates, extract a fresh folder and use the new copy.
Settings at `%LOCALAPPDATA%\MindDump\settings.json` and vault notes are retained.
Uninstall the installed version through Windows Installed apps. No Inno Setup
installation is needed on the target PC.

## Development

The project targets `.NET 10` and WinUI 3 through Windows App SDK `2.1.3`.

```powershell
dotnet build MindDump.slnx -c Debug
dotnet test --solution MindDump.slnx -c Debug
dotnet publish src\MindDump\MindDump.csproj -c Release -p:PublishProfile=win-x64
```

The release publish output is an unpackaged, self-contained `win-x64` application.

For the per-user Windows installer, repeatable build command, and upgrade/uninstall
test checklist, see [installer/README.md](installer/README.md).

Build all release assets with `pwsh -NoProfile -File scripts/Build-Release.ps1`.
See [public distribution](docs/distribution.md) for version/tag rules, draft-release
automation, signing research, and the update/MSIX decisions.
For the selected interim self-signed local build, see
[self-signing instructions](docs/self-signing.md). Self-signing does not establish
public publisher trust; automated GitHub builds remain unsigned.
