#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif

#define MyAppName "K-Zone"
#define MyAppPublisher "KrisB"
#define MyAppExeName "K-Zone.exe"

[Setup]
AppId={{7A2F4B1C-9E3D-4F6A-B8C2-1D5E7F9A3B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=K-Zone.Setup.{#MyAppVersion}
SetupIconFile=src\KrisZone\Resources\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 만들기"; GroupDescription: "추가 옵션:"
Name: "startupentry"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 옵션:"; Flags: unchecked

[Files]
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "K-Zone 실행"; Flags: nowait postinstall skipifsilent
