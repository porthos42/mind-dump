; Build through scripts/Build-Installer.ps1. Keep AppId stable across releases.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{A1699B28-20B9-464D-A78A-9F0594C80487}
AppName=MindDump
AppVersion={#AppVersion}
AppPublisher=MindDump
AppPublisherURL=https://github.com/porthos42/mind-dump
DefaultDirName={localappdata}\Programs\MindDump
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=MindDump-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\MindDump\Assets\MindDump.ico
UninstallDisplayIcon={app}\MindDump.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
SetupMutex=MindDumpInstaller
#ifdef DevelopmentSigning
SignTool=mddevelopment
SignedUninstaller=yes
SignedUninstallerDir={#SigningCache}
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\MindDump"; Filename: "{app}\MindDump.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\MindDump"; Filename: "{app}\MindDump.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\MindDump.exe"; Description: "Launch MindDump (system tray)"; Flags: nowait postinstall skipifsilent unchecked

[Code]
// Also detects old portable builds, which do not expose an application mutex.
// Fail closed if process enumeration is unavailable. Never terminate a process.
function CanModifyApplication(): Boolean;
var
  Locator, Services, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''MindDump.exe''');
    Result := Processes.Count = 0;
    if not Result then
      Log('MindDump is running; no files will be changed.');
  except
    Log('Cannot verify whether MindDump is running: ' + GetExceptionMessage);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CanModifyApplication() then
    Result := 'Save or copy any unsaved MindDump input, then choose Exit from the MindDump tray menu. Close all portable copies too, then click Install to retry. If MindDump is already closed, Windows process detection may be unavailable; cancel setup and try again after resolving it.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := CanModifyApplication();
  if not Result then
    SuppressibleMsgBox('Uninstall cannot continue. Save or copy any unsaved MindDump input and choose Exit from its tray menu, then run uninstall again. If the app is already closed, Windows process detection may be unavailable. Your settings and notes have not been changed.', mbError, MB_OK, IDOK);
end;
