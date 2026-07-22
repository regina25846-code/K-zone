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

[Code]
// K-앱 공통 설치 흐름 표준 5단계 (2026-07-19 확정, K-Clock 기준) 적용:
//  1) 이미 설치돼있으면 실행 시 제거/유지 선택
//  2) 제거 선택 시 애플리케이션 데이터 삭제 여부 확인(기본은 삭제 안 함)
//  3) 프로그램 실행 중이면 종료 확인창
//  4) 설치 위치 표시 (Inno 기본 DirPage — 별도 설정 불필요)
//  5) 완료 화면에 프로그램 실행 + 바탕화면 바로가기 체크란 ([Run]/[Tasks]에 이미 있음)

const
  // AppId({{7A2F4B1C-...})에 대응하는 Inno 언인스톨 레지스트리 키. PrivilegesRequired=lowest라 HKCU.
  UninstallRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7A2F4B1C-9E3D-4F6A-B8C2-1D5E7F9A3B6C}_is1';

function GetUninstallString(): String;
var
  s: String;
begin
  s := '';
  if not RegQueryStringValue(HKCU, UninstallRegKey, 'UninstallString', s) then
    RegQueryStringValue(HKLM, UninstallRegKey, 'UninstallString', s);
  Result := s;
end;

// 1단계: 이미 설치돼있으면 제거/유지부터 물어봄
function InitializeSetup(): Boolean;
var
  uninst: String;
  code: Integer;
begin
  Result := True;
  uninst := GetUninstallString();
  if uninst <> '' then
  begin
    if MsgBox('K-Zone이 이미 설치되어 있습니다.'#13#10#13#10'기존 버전을 제거하시겠습니까?'#13#10#13#10'[예] 제거 후, Setup을 다시 실행해 새로 설치합니다.'#13#10'[아니오] 제거하지 않고 이 위에 덮어 설치(업데이트)합니다.',
       mbConfirmation, MB_YESNO) = IDYES then
    begin
      // 언인스톨러를 UI와 함께 실행 → 그 안에서 2단계(데이터 삭제 확인)까지 이어짐. 끝나면 설치는 중단.
      Exec(RemoveQuotes(uninst), '', '', SW_SHOW, ewWaitUntilTerminated, code);
      Result := False;
    end;
  end;
end;

// 3단계 보조: K-Zone.exe가 실행 중인지 tasklist로 확인
function IsAppRunning(): Boolean;
var
  code: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq {#MyAppExeName}" | find /I "{#MyAppExeName}"',
    '', SW_HIDE, ewWaitUntilTerminated, code);
  Result := (code = 0);
end;

// 3단계: 파일 복사 직전, 실행 중이면 종료 확인 후 강제 종료(트레이 상주라 RestartManager가 못 잡음)
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  code: Integer;
begin
  Result := '';
  if IsAppRunning() then
  begin
    if MsgBox('K-Zone이 실행 중입니다.'#13#10'종료하고 설치를 계속하시겠습니까?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, code);
      Sleep(600); // 프로세스 종료 후 OS가 파일 핸들 풀 시간 확보
    end
    else
      Result := '설치가 취소되었습니다. K-Zone을 종료한 후 다시 시도해 주세요.';
  end;
end;

// 2단계: 제거 완료 시점에 애플리케이션 데이터 삭제 여부 확인(기본은 [아니오] = 보존)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('설치 대상과 함께 K-Zone 설정 데이터(레이아웃, 환경설정 등)도 삭제하시겠습니까?'#13#10#13#10'[아니오]를 선택하면 데이터는 보존됩니다.',
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\K-Zone'), True, True, True);
    end;
  end;
end;
