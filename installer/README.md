# Windows installer

MindDump uses Inno Setup for a per-user, offline Windows x64 installer.
It installs the full self-contained publish tree into
`%LOCALAPPDATA%\Programs\MindDump`, creates a Start menu shortcut and an
Installed apps uninstall entry, and offers an unchecked desktop shortcut.
It does not configure Windows sign-in startup. Launching after setup is optional
and unchecked; MindDump appears in the system tray.

## Build

Install the .NET SDK selected by `global.json` and
[Inno Setup 7](https://jrsoftware.org/isdl.php) (6 is also supported by the build script), then run from the repository:

```powershell
pwsh -NoProfile -File scripts/Build-Installer.ps1
# For a nonstandard compiler location:
pwsh -NoProfile -File scripts/Build-Installer.ps1 -IsccPath 'C:\Tools\Inno Setup 7\ISCC.exe'
```

The script reads `Version` from `src/MindDump/MindDump.csproj` (currently
`0.1.1`), publishes to a fresh directory, validates the executable version and
required WinUI/runtime files, then embeds the entire directory recursively.
Update that one Version property to match the next release tag, without its `v`.
The existing explicit XBF/PRI publish target must remain intact.

Outputs under `artifacts/installer-<version>/` are ignored by Git:

- `MindDump-<version>-win-x64-Setup.exe`
- `SHA256SUMS.txt` (installer checksum)
- `publish-SHA256SUMS.txt` (all payload file checksums)
- `installer-build.log` (compiler file inclusion log)
- A unique `publish-...` directory containing the self-contained payload

Verify the checksum with `Get-FileHash <installer.exe> -Algorithm SHA256`.
For a combined portable ZIP, installer and checksum build, use
`scripts/Build-Release.ps1`; see [distribution](../docs/distribution.md) for
tag validation, draft automation and signing plans.
By default the installer is unsigned; Windows may show an unknown publisher/SmartScreen
warning. No certificate or release publication is part of this build process.
Optional local self-signing signs the app, setup and uninstaller; see
[self-signing](../docs/self-signing.md). It does not establish public trust.

## Upgrade, running applications, and data

Keep the installer AppId and installation directory stable. Run a newer setup
as the same Windows user over the existing installation; no prior uninstall is
required. All shipped files are replaced, regardless of individual DLL versions.
There is no automatic update service or downgrade prevention.

Before copying or uninstalling, setup checks Windows process information for
`MindDump.exe`, including portable copies. If any are running, it refuses to
proceed and asks the user to save/copy their draft and use tray **Exit**. Retry
setup after exiting; rerun uninstall after exiting. Silent installation also
fails this guard rather than closing the application. If process detection is
unavailable, the operation is blocked. Setup never requests application closure
through Restart Manager and never kills a process. Do not relaunch MindDump
while installation/uninstallation is in progress; file-lock errors should be
aborted and retried after closing the app.

`SettingsService` stores settings at `%LOCALAPPDATA%\MindDump\settings.json`,
outside the installation directory. No installer action writes to or deletes
that directory or the configured Obsidian vault. Uninstall removes installed
application files and shortcuts while retaining settings and captured notes.
Do not put notes or settings in the installation directory. Portable copies
are not removed; avoid running portable and installed copies simultaneously.
Future releases that remove/rename shipped files should explicitly retire those
specific files; do not add wildcard deletion of the app or user-data directories.

## Manual test checklist (another Windows x64 PC)

Use a standard Windows account, a test vault, and backup settings/notes first.

1. Install with default options: no elevation, Start menu shortcut present,
   desktop shortcut absent, app listed in Installed apps, no startup entry.
2. Confirm `App.xbf`, `MainWindow.xbf`, `MindDump.pri`, `coreclr.dll`, and
   `Microsoft.UI.Xaml.dll` exist in the installation folder. Launch from Start,
   verify tray/hotkey, select the test vault, capture, and confirm Markdown sync.
3. Run setup again with unsaved capture input (also try a portable copy running).
   Confirm it blocks, the process stays alive, and the draft remains. Save or
   copy the draft, exit via tray, retry, and confirm installation completes.
4. Reinstall with the desktop task selected. Confirm the shortcut works.
   For a true version upgrade, repeat using the next version's installer over
   this one; verify settings/vault selection and previous notes remain intact.
5. Attempt uninstall while a draft is open. Confirm it blocks without killing
   the app. Exit, uninstall through Installed apps, and confirm program files,
   shortcuts, and the uninstall entry are removed while settings and notes remain.
6. Reinstall and verify retained settings load. Check a Windows machine with no
   .NET/Windows App SDK development tools to exercise self-contained deployment.

Building and inspecting a payload do not establish installation, upgrade,
uninstall, or clean-machine runtime success. Record those results separately.

## Initial 0.1.0 installer verification

- Self-contained Release publish and Inno Setup 7 compilation succeeded.
- All 514 payload files were matched against the compiler inclusion log,
  including `App.xbf`, `MainWindow.xbf`, `MindDump.pri`, and runtime dependencies.
- Published executable reports product version `0.1.0` (plus source commit).
- The existing test executable passed all 15 tests during this implementation.
- PowerShell script parsing and `git diff --check` passed.
- NuGet vulnerability metadata could not be fetched in this environment (NU1900);
  publishing completed using locally available packages.
- Windows process-information access was denied in the agent sandbox; running-app
  protection was subsequently confirmed through user manual testing.
- User-reported manual checks passed: installation without elevation, shortcuts,
  installed-app capture, and setup blocking while unsaved input remained open.
- Same-version (0.1.0 over 0.1.0) reinstall passed with settings, vault selection,
  and notes preserved, and new captures working.
- User confirmed uninstall and subsequent reinstall passed, with settings and
  notes retained and retained settings loading after reinstall.
- A true newer-version upgrade remains pending until the next release. The
  manual results above do not establish that distinct-version upgrade behavior.

## Self-signed installer verification (2026-11-10)

- User reported the local self-signed build succeeded after correcting detection
  of Windows' untrusted-root signature result.
- On another Windows PC, user confirmed installation over the existing 0.1.0
  installation after exiting MindDump, successful launch, retained vault
  selection, and successful note capture.
- User confirmed uninstall preserved settings and vault notes.
- This was a same-version reinstall. A true newer-version upgrade and timestamped
  signing remain untested. Running-app protection with unsaved input was tested
  previously; it was not separately reported in this self-signed test sequence.

## 0.1.0 to 0.1.1 upgrade checklist

Status: steps 1 through 4 passed, as reported by the owner on 2026-11-10.
The post-upgrade uninstall/reinstall check in step 5 remains pending.
Keep the existing installation; do not uninstall before this test.

1. Confirm Installed apps reports MindDump 0.1.0 and the installed
   `%LOCALAPPDATA%\Programs\MindDump\MindDump.exe` reports product version 0.1.0.
   Launch that installed copy, confirm the configured vault, and capture a
   uniquely named pre-upgrade note. Back up settings.json and the vault notes
   outside the installation directory.
2. Leave a recognizable unsaved draft open. Run the 0.1.1 setup and confirm
   it blocks, leaves MindDump running, and preserves the draft. Save/copy the
   draft, exit through the tray, and retry setup without uninstalling 0.1.0.
3. Confirm Installed apps has one MindDump entry at 0.1.1, the installed EXE
   reports 0.1.1, and the existing Start menu/desktop shortcuts launch it.
4. Confirm the vault selection and settings are retained, all previous notes
   remain intact, and a new uniquely named post-upgrade capture succeeds.
5. Exit and uninstall 0.1.1. Confirm app files/shortcuts/registration are removed
   while settings and notes remain. Reinstall 0.1.1 and confirm retained settings
   load and capture still works.

Record actual results before pushing `v0.1.1`. Do not infer upgrade success
from build validation or the earlier same-version reinstall.

### Reported upgrade results (2026-11-10)

- Installed baseline: `0.1.0+5b43ef4dae4e5777aca63e130ae62973e80e6e7f`.
- Setup blocked with an unsaved draft open and preserved the draft.
- After saving/copying the draft and tray Exit, setup upgraded the existing
  installation without an uninstall.
- Retained vault selection and old notes were confirmed; a new capture passed.
- Installed result: `0.1.1+b37f7ea66b2f50979b21628a1c51f7675e0e5df2`.
  This local test build used the version bump over the then-current HEAD;
  the tag build will contain the eventual release commit identifier.
- These results correspond to the five steps provided in chat. They do not
  establish a new uninstall/reinstall test or all shortcut/registration checks.
