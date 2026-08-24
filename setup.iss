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
// K-Zone은 다른 창을 관리하는 특성상 형이 앱 자체를 관리자 권한으로 켜놓고 쓰는 경우가
// 있는데, 그러면 일반 권한 설치 프로그램이 실행 중인 K-Zone을 강제 종료하지 못해 설치가
// 막힘. 자체적으로 "관리자 권한으로 재시작할까요?" 되묻는 방식(1.2.1-4)은 UAC 창이 다른
// 창 뒤에 가려지는 등 오히려 더 헷갈리고 불안정했음(2026-08-06 형 실기 확인) — 대신 설치
// 프로그램 자체를 처음부터 항상 관리자 권한(admin)으로 요구해서, 표준 윈도우 UAC 확인창이
// 설치 시작 시 한 번만 뜨고 그 뒤로는 무조건 관리자 권한이라 종료 실패 자체가 안 생기게 함.
// 설치된 K-Zone.exe 자체는 그대로 asInvoker라 평소 실행엔 전혀 영향 없음.
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
// ── 업그레이드 시 설치 위치 처리 (2026-08-25) ──────────────────────────────────
// UsePreviousAppDir은 원래 기본값이 yes지만, 아래 DisableDirPage=no와 짝이라 명시해둔다.
// AppId가 고정이라 Inno가 이전 설치의 언인스톨 레지스트리 키에서 예전 설치 경로를 읽어
// 설치 위치 입력란에 그대로 미리 채워준다 → 사용자가 [다음]만 누르면 항상 같은 자리에
// 덮어쓰기 업그레이드가 된다(폴더가 갈라져 두 벌 설치되는 사고 방지).
UsePreviousAppDir=yes
// ⚠ Inno Setup 6의 DisableDirPage 기본값은 auto이고, auto는 "이전 설치가 감지되면
// 설치 위치 화면을 건너뛴다"는 뜻이다. 예전엔 InitializeSetup에서 구버전을 먼저 제거해
// 레지스트리 키가 사라진 뒤라 이 화면이 떴는데, 그 제거 단계를 폐지(2026-08-07 표준)한
// 지금은 업그레이드할 때마다 이 화면이 통째로 사라진다 — K-앱 공통 설치 흐름 표준의
// "설치 위치 보여줌" 항목이 조용히 깨지는 것. no로 못박아 항상 보이게 한다.
DisableDirPage=no
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

// Inno 기본 "정말 제거하시겠습니까?" 확인창(ConfirmUninstall)이 데이터 안전 여부를 전혀
// 언급 안 해서, 실제로는 다음 화면(체크박스, 기본 미체크)에서만 데이터가 지워지는데도
// 이 화면만 보면 전부 지워지는 것처럼 읽힘 — 형이 과거 실제 데이터 손실을 겪은 뒤로 이
// 문구를 볼 때마다 불안해함(2026-08-06). 코드가 아니라 사람의 설명에 의존하지 않도록
// 다이얼로그 문구 자체에 안전 여부를 명시.
[Messages]
ConfirmUninstall=%1의 프로그램 파일을 제거합니다.%n%n레이아웃·설정 등 저장된 데이터는 다음 화면에서 별도로 체크하지 않는 한 삭제되지 않습니다.%n%n계속하시겠습니까?

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
// K-앱 공통 설치 흐름 표준 (2026-08-07 개정) 적용:
//  1) 이미 설치돼있어도 별도 제거/유지 선택창 없이 곧바로 덮어써서 업그레이드한다
//     (크롬·VS코드 등도 쓰는 표준 방식 — AppId 고정으로 Inno가 알아서 같은 설치 위치에
//     덮어씀, [Files]의 ignoreversion으로 파일 교체). 예전엔 "제거하시겠습니까?" 확인창을
//     먼저 띄웠는데, 실제로 필요한 기능이 아니었고 오히려 형이 8월4일 실제 데이터 손실을
//     겪은 뒤로 이 창을 볼 때마다 불안해해서(8월6일 재발) 2026-08-07에 폐지함
//     ([[feedback_destructive_dialog_wording_precision]] 참고, 다른 K-앱들과 동일 조치).
//     이 창이 실행하던 "구버전 제거 프로그램을 Exec로 직접 실행"하는 경로 자체가
//     사라졌으므로, 이제 데이터 삭제는 사용자가 [프로그램 제거]에서 직접 제거할 때
//     체크박스(기본 미체크)를 스스로 체크하는 경우 외엔 발생할 수 없다.
//  2) 프로그램 실행 중이면 종료 확인창
//  3) 설치 위치 표시 (Inno 기본 DirPage. ⚠ 단, 1)에서 "먼저 제거하기"를 없앤 뒤로는
//     [Setup]에 DisableDirPage=no를 명시해야만 뜬다 — 기본값 auto가 "이전 설치가 있으면
//     이 화면 건너뛰기"라서, 업그레이드할 땐 항상 건너뛰어버리기 때문. 위 [Setup] 참고)
//  4) 완료 화면에 프로그램 실행([Run] 기본 제공) + 바탕화면 바로가기 체크란(아래 커스텀 컨트롤,
//     기본 체크)

// 2단계 보조: K-Zone.exe가 실행 중인지 tasklist로 확인
function IsAppRunning(): Boolean;
var
  code: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq {#MyAppExeName}" | find /I "{#MyAppExeName}"',
    '', SW_HIDE, ewWaitUntilTerminated, code);
  Result := (code = 0);
end;

// 2단계: 파일 복사 직전, 실행 중이면 종료 확인 후 강제 종료(트레이 상주라 RestartManager가 못 잡음)
// 0.6초 한 번만 기다리고 넘어가면, 안티바이러스 스캔 지연이나 무언가가 곧바로 재실행시키는
// 경우 파일 핸들이 아직 안 풀려서 DeleteFile 액세스 거부로 설치가 깨졌다(2026-08-04 형이 실제
// 재현). 죽었는지 다시 확인하면서 여러 번 재시도하도록 강화한다.
// 2026-08-06: 관리자 권한으로 실행 중인 K-Zone은 일반 권한 taskkill/Stop-Process로 절대
// 종료가 안 된다는 걸 형이 실기로 확인(재시도 8회+PowerShell 병행해도 100% 실패) — Setup
// 자신이 관리자 권한으로 재시작할지 되묻는 방식도 시도했지만 UAC 창이 안 보이는 등 오히려
// 더 헷갈려서 롤백함. 대신 [Setup]에 PrivilegesRequired=admin을 넣어 Setup.exe 자체가
// 처음부터 항상 관리자 권한으로 뜨도록 바꿔서, 이 함수가 실행되는 시점엔 이미 무조건 관리자
// 권한이라 대상이 관리자 권한이든 아니든 taskkill 한 번이면 사실상 항상 성공함 — 그래도
// 혹시 남는 극단적 케이스(느린 핸들 해제 등) 대비로 재시도만 유지.
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
      while IsAppRunning() and (attempt < 8) do
      begin
        Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, code);
        if IsAppRunning() then
          Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
            '-NoProfile -Command "Get-Process -Name ''K-Zone'' -ErrorAction SilentlyContinue | Stop-Process -Force"',
            '', SW_HIDE, ewWaitUntilTerminated, code);
        Sleep(1100); // 프로세스 종료 후 OS가 파일 핸들 풀 시간 확보
        attempt := attempt + 1;
      end;
      if IsAppRunning() then
        Result := 'K-Zone을 종료하지 못했습니다.'#13#10'작업 관리자에서 K-Zone을 직접 종료한 후 설치를 다시 실행해 주세요.';
    end
    else
      Result := '설치가 취소되었습니다. K-Zone을 종료한 후 다시 시도해 주세요.';
  end;
end;

// 4단계 보조: 완료 화면에 "바탕화면에 바로가기 만들기" 체크박스를 직접 그려 넣는다.
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

// [제거할 때] 애플리케이션 데이터 삭제 여부를 체크박스(기본 체크 해제)로 확인.
// (설치 흐름 1~4단계와는 별개 — 사용자가 [프로그램 제거]를 직접 실행했을 때만 탄다)
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

    // ── 자동 실행 흔적 정리 (2026-08-25 추가) ────────────────────────────────
    // K-Zone은 설정에서 "자동 실행"을 켜면 App.xaml.cs의 SetAutoStart가 런타임에
    // 아래 3가지를 직접 만든다. [Registry]의 uninsdeletevalue는 "설치할 때 [Tasks]
    // 체크로 만들어진 항목"만 지우므로, 앱 안에서 켠 경우엔 제거 후에도 그대로 남아
    // 부팅 때마다 없어진 exe를 실행하려 든다 — 여기서 전부 정리한다.
    // (데이터가 아니라 쓰레기 항목이므로 아래 체크박스와 무관하게 항상 지운다)
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');
    RegDeleteValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', '{#MyAppName}');
    DeleteFile(ExpandConstant('{userstartup}\{#MyAppName}.lnk'));

    if DeleteDataOnUninstall then
    begin
      // 설정·레이아웃 (SettingsManager: LocalApplicationData\K-Zone\settings.json)
      DelTree(ExpandConstant('{localappdata}\{#MyAppName}'), True, True, True);
      // 크래시 로그 (App.xaml.cs WriteCrashLog: ApplicationData(Roaming)\K-Zone\crash.log)
      // — 저장 위치가 두 군데로 갈려 있어서 여기까지 지워야 "데이터 삭제"가 실제로 완전해진다.
      DelTree(ExpandConstant('{userappdata}\{#MyAppName}'), True, True, True);
    end;
  end;
end;
