; Inno Setup script for Junior Torrent Client (JTC)
; Compile with: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\JTC.iss
; Output: dist\JTC-v0.3.17.1-setup.exe

#define MyAppName "Junior Torrent Client"
#define MyAppShortName "JTC"
#define MyAppVersion "0.3.17.1"
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
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
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
; Leave the app data (%LocalAppData%\TClient) alone by default — user's downloads / torrent list.
; Just remove the install directory itself; other stray files (debug.log etc.) live in AppData
; and are the user's to keep or delete.
Type: filesandordirs; Name: "{app}"
