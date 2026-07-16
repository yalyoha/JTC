; Inno Setup script for Junior Torrent Client (JTC)
; Compile with: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\JTC.iss
; Output: dist\JTC-v0.3.36-setup.exe

#define MyAppName "Junior Torrent Client"
#define MyAppShortName "JTC"
#define MyAppFolderName "JuniorTorrentClient"
#define MyAppVersion "0.3.36"
#define MyAppPublisher "yalyoha"
#define MyAppURL "https://github.com/yalyoha/JTC"
#define MyAppExeName "JTC.exe"
#define MyAppSourceDir "..\src\JTC\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; Unique GUID — do not change once released (identifies the app in "Programs and Features").
; Same GUID as pre-rebrand, so a user upgrading from a TClient install won't get a duplicate entry.
AppId={{4F1B7EDF-9D9D-4C88-9E7A-4A3F1E7B2F91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
; Per-user install — no UAC prompt, works without admin.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#MyAppFolderName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Ignore the previous InstallLocation / GroupName recorded in the registry.
; Without these, a machine upgrading from the pre-rebrand "TClient" install would
; keep installing into %LocalAppData%\Programs\TClient because Inno remembers the old path.
UsePreviousAppDir=no
UsePreviousGroup=no
; Where the installer .exe goes when built.
OutputDir=..\dist
OutputBaseFilename={#MyAppShortName}-v{#MyAppVersion}-setup
SetupIconFile=..\src\JTC\Assets\tclient.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
; Windows 10 20H1+ / Windows 11
MinVersion=10.0.19041
; Detect an already-running JTC via its SingleInstance mutex (see SingleInstance.cs).
; Must match the exact name — "Local\" scope, per-user.
AppMutex=Local\JTC-SingleInstance-yalyoha
; Inno's built-in CloseApplications sends WM_CLOSE via Restart Manager, which our
; OnAppWindowClosing absorbs (window hides to tray instead of exiting), leaving
; JTC.exe locked and the installer hanging / crashing. Instead we run our own
; PrepareToInstall in [Code] that drops the shutdown marker into JTC's inbox and
; waits for the mutex to release — the app then hard-exits via ShutdownRequested.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The whole publish folder — 400+ WinAppSDK / MonoTorrent / .NET DLLs.
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leave the app data (%LocalAppData%\JTC, and %LocalAppData%\TClient for old installs) alone
; by default — that's the user's downloads / torrent list. Just remove the install directory.
Type: filesandordirs; Name: "{app}"

[Code]
const
  JtcMutex        = 'Local\JTC-SingleInstance-yalyoha';
  ShutdownTimeout = 10000; // ms
  PollInterval    = 200;

// GetTickCount isn't in Inno 6's built-in Pascal namespace, so import directly
// from Win32. Used only to give the shutdown marker file a unique name in case a
// previous install left a stale marker behind.
function GetTickCount: DWord;
  external 'GetTickCount@kernel32.dll stdcall';

// Called by Inno right before the install phase. If JTC is running (holds the
// SingleInstance mutex), drop the "@shutdown" marker into its inbox and wait for
// the mutex to release. On timeout, return a non-empty error string — Inno shows
// it in a dialog and aborts the install without touching any files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  MarkerDir:  String;
  MarkerPath: String;
  Elapsed:    Integer;
begin
  Result := '';
  if not CheckForMutexes(JtcMutex) then
    Exit;

  MarkerDir := ExpandConstant('{localappdata}\JTC\inbox');
  if not DirExists(MarkerDir) then
    ForceDirectories(MarkerDir);
  MarkerPath := MarkerDir + '\shutdown_' + IntToStr(GetTickCount) + '.txt';
  if not SaveStringToFile(MarkerPath, '@shutdown', False) then
  begin
    Result := 'Не удалось создать файл-сигнал завершения работы JTC в ' + MarkerDir;
    Exit;
  end;

  Elapsed := 0;
  while (Elapsed < ShutdownTimeout) and CheckForMutexes(JtcMutex) do
  begin
    Sleep(PollInterval);
    Elapsed := Elapsed + PollInterval;
  end;

  if CheckForMutexes(JtcMutex) then
    Result := 'JTC не завершил работу за отведённое время. ' +
              'Закройте его вручную через меню трея (правый клик по иконке → Выход) и запустите установку заново.';
end;
