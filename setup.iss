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
Name: "startupentry"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 옵션:"; Flags: unchecked

[Files]
Source: "publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "K-Zone 실행"; Flags: nowait postinstall skipifsilent

[Code]
// K-앱 공통 설치 흐름 표준 5단계 (2026-07-19 확정, K-Clock 기준) 적용:
//  1) 이미 설치돼있으면 실행 시 제거/유지 선택
//  2) 제거 선택 시 애플리케이션 데이터 삭제 여부 확인(체크박스, 기본 체크 해제)
//  3) 프로그램 실행 중이면 종료 확인창
//  4) 설치 위치 표시 (Inno 기본 DirPage — 별도 설정 불필요)
//  5) 완료 화면에 프로그램 실행([Run] 기본 제공) + 바탕화면 바로가기 체크란(아래 커스텀 컨트롤,
//     기본 체크) — 바탕화면 바로가기는 원래 그보다 앞 단계인 "추가 작업 선택" 화면의
//     [Tasks] 항목이었는데, 표준 문서상 완료 화면에 있어야 해서 옮김(2026-08-04 형 확인).

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
// 0.6초 한 번만 기다리고 넘어가면, 안티바이러스 스캔 지연이나 무언가가 곧바로 재실행시키는
// 경우 파일 핸들이 아직 안 풀려서 DeleteFile 액세스 거부로 설치가 깨졌다(2026-08-04 형이 실제
// 재현). 죽었는지 다시 확인하면서 여러 번 재시도하도록 강화한다.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  code, attempt: Integer;
begin
  Result := '';
  if IsAppRunning() then
  begin
    if MsgBox('K-Zone이 실행 중입니다.'#13#10'종료하고 설치를 계속하시겠습니까?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      attempt := 0;
      while IsAppRunning() and (attempt < 5) do
      begin
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, code);
        Sleep(800); // 프로세스 종료 후 OS가 파일 핸들 풀 시간 확보
        attempt := attempt + 1;
      end;
      if IsAppRunning() then
        Result := 'K-Zone을 종료하지 못했습니다.'#13#10'프로그램을 직접 종료한 후 설치를 다시 실행해 주세요.';
    end
    else
      Result := '설치가 취소되었습니다. K-Zone을 종료한 후 다시 시도해 주세요.';
  end;
end;

// 5단계 보조: 완료 화면에 "바탕화면에 바로가기 만들기" 체크박스를 직접 그려 넣는다.
// Inno의 [Tasks]는 완료 화면이 아니라 그 앞 "추가 작업 선택" 화면에 뜨므로, 완료 화면에
// 놓으려면 [Icons]로 선언하는 대신 여기서 WScript.Shell로 직접 바로가기를 만든다.
var
  FinishDesktopCheck: TNewCheckBox;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    // RunList가 완료 페이지 바닥까지 고정폭(항목 1개짜리 [Run]엔 과한 높이)을 차지하고 있어서
    // 그 아래에 그냥 이어붙이면 페이지 밖으로 밀려나 안 보인다(2026-08-04 오푸스가 Inno 원본
    // .dfm 좌표까지 대조해서 확인) — RunList 자체를 줄이고 그 자리에 체크박스를 넣는다.
    WizardForm.RunList.Height := WizardForm.RunList.Height - ScaleY(25);
    FinishDesktopCheck := TNewCheckBox.Create(WizardForm);
    FinishDesktopCheck.Parent := WizardForm.FinishedPage;
    FinishDesktopCheck.Left := WizardForm.RunList.Left;
    FinishDesktopCheck.Top := WizardForm.RunList.Top + WizardForm.RunList.Height + ScaleY(8);
    FinishDesktopCheck.Width := WizardForm.RunList.Width;
    FinishDesktopCheck.Height := ScaleY(17);
    FinishDesktopCheck.Caption := '바탕화면에 바로가기 만들기';
    FinishDesktopCheck.Checked := True;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  WshShell, Shortcut: Variant;
begin
  Result := True;
  if (CurPageID = wpFinished) and (FinishDesktopCheck <> nil) and FinishDesktopCheck.Checked then
  begin
    WshShell := CreateOleObject('WScript.Shell');
    Shortcut := WshShell.CreateShortcut(ExpandConstant('{autodesktop}\{#MyAppName}.lnk'));
    Shortcut.TargetPath := ExpandConstant('{app}\{#MyAppExeName}');
    Shortcut.WorkingDirectory := ExpandConstant('{app}');
    Shortcut.Save;
  end;
end;

// 2단계: 애플리케이션 데이터 삭제 여부를 체크박스(기본 체크 해제)로 확인.
// 예/아니오 팝업이었던 걸 K-Clock과 같은 체크박스 방식으로 통일(2026-08-04 형 요청).
// Inno 기본 "정말 제거하시겠습니까?" 확인창은 InitializeUninstall 직후에 뜨므로, 우리
// 체크박스 창을 거기 겹치게 InitializeUninstall에서 띄우면 확인창이 연달아 두 번 뜬다
// (2026-08-04 오푸스 리뷰 지적). 그래서 기본 확인창 다음 단계인 usUninstall에서 띄운다.
var
  DeleteDataOnUninstall: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Form: TSetupForm;
  Lbl: TNewStaticText;
  ChkDeleteData: TNewCheckBox;
  BtnOK: TNewButton;
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteDataOnUninstall := False;
    // Inno Setup 6.6.0부터 CreateCustomForm이 크기 인자를 받는 형태로 바뀌었다
    // (2026-08-04 오푸스 리뷰가 실제 Inno 소스 대조로 확인) — 인자 없이 부르면 CI 빌드 자체가 깨짐.
    Form := CreateCustomForm(ScaleX(380), ScaleY(190), False, True);
    try
      Form.Caption := 'K-Zone 제거';
      Form.Position := poScreenCenter;

      Lbl := TNewStaticText.Create(Form);
      Lbl.Parent := Form;
      Lbl.Left := ScaleX(16);
      Lbl.Top := ScaleY(16);
      Lbl.Width := Form.ClientWidth - ScaleX(32);
      Lbl.AutoSize := False;
      Lbl.Height := ScaleY(56);
      Lbl.WordWrap := True;
      Lbl.Caption := 'K-Zone을 제거합니다.'#13#10'아래 항목을 선택하지 않으면 설정 데이터(레이아웃, 환경설정 등)는 보존됩니다.';

      ChkDeleteData := TNewCheckBox.Create(Form);
      ChkDeleteData.Parent := Form;
      ChkDeleteData.Left := ScaleX(16);
      ChkDeleteData.Top := ScaleY(88);
      ChkDeleteData.Width := Form.ClientWidth - ScaleX(32);
      ChkDeleteData.Height := ScaleY(17);
      ChkDeleteData.Caption := '설치 대상과 애플리케이션 데이터 삭제';
      ChkDeleteData.Checked := False;

      BtnOK := TNewButton.Create(Form);
      BtnOK.Parent := Form;
      BtnOK.Width := ScaleX(75);
      BtnOK.Height := ScaleY(23);
      BtnOK.Left := Form.ClientWidth - ScaleX(16) - BtnOK.Width;
      BtnOK.Top := Form.ClientHeight - ScaleY(16) - BtnOK.Height;
      BtnOK.Caption := '확인';
      BtnOK.ModalResult := mrOk;
      BtnOK.Default := True;

      Form.ActiveControl := BtnOK;
      Form.ShowModal();
      DeleteDataOnUninstall := ChkDeleteData.Checked;
    finally
      Form.Free;
    end;
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    // 바탕화면 바로가기는 이제 [Icons]가 아니라 완료 화면에서 스크립트로 직접 만들기 때문에
    // Inno의 자동 제거 대상이 아니다 — 여기서 직접 지운다(있으면).
    DeleteFile(ExpandConstant('{autodesktop}\{#MyAppName}.lnk'));
    if DeleteDataOnUninstall then
      DelTree(ExpandConstant('{localappdata}\K-Zone'), True, True, True);
  end;
end;
