; Build with build-installer.ps1. Keep AppId stable across releases.
#ifndef AppSourceDir
  #error AppSourceDir must point to the compiled application directory.
#endif
#define AppVersion GetVersionNumbersString(AppSourceDir + "\Perfview.exe")
#define AppName "Perfview"
#define AppId "PerfviewTaskbar"
#define StartupValue "PerfviewTaskbar"
#define AppMutexName "Local\PerfviewTaskbar.v1"
; Integration tests use a separate installation identity, shortcuts and startup value.
#ifdef TestInstance
  #define AppName "Perfview Installer Test " + TestInstance
  #define AppId "PerfviewTaskbar.Test." + TestInstance
  #define StartupValue AppId
  #define AppMutexName "Local\" + AppId
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={localappdata}\Programs\{#AppName}
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
AppMutex={#AppMutexName}
SetupMutex=Local\{#AppId}.Setup
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Perfview.exe
SetupIconFile=..\assets\Perfview.ico
OutputBaseFilename=Perfview-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppSourceDir}\Perfview.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSourceDir}\Perfview.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSourceDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\Perfview.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\Perfview.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Keep an existing opt-in working when switching from a portable copy.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#StartupValue}"; ValueData: """{app}\Perfview.exe"""; Check: StartupEnabled

[Run]
Filename: "{app}\Perfview.exe"; Description: "Launch {#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('Perfview requires .NET Framework 4.8 or later. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48, then run Setup again.', mbError, MB_OK, IDOK);
end;

function StartupEnabled: Boolean;
begin
  Result := RegValueExists(HKCU, StartupKey, '{#StartupValue}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, InstalledExe: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledExe := ExpandConstant('{app}\Perfview.exe');
    if RegQueryStringValue(HKCU, StartupKey, '{#StartupValue}', Command) then
      if (CompareText(Trim(Command), '"' + InstalledExe + '"') = 0) or
         (CompareText(Trim(Command), InstalledExe) = 0) then
        RegDeleteValue(HKCU, StartupKey, '{#StartupValue}');
  end;
end;
