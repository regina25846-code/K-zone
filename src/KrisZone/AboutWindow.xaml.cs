using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Windows;

namespace KrisZone
{
    public partial class AboutWindow : Window
    {
        private string _currentVersion = "0.0.0";

        public AboutWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, _) => DragMove();
            // AssemblyVersion(GetName().Version)은 csproj의 <Version>과 별개 필드라 테스트
            // 빌드마다 안 올리면 항상 옛날 값(1.1.8)만 나온다. <Version>의 "-4" 같은 접미사까지
            // 그대로 반영되는 AssemblyInformationalVersion을 대신 읽는다(2026-08-04 형이 실제
            // 재현 — 1.1.8-4를 설치했는데 프로그램 정보엔 계속 1.1.8로만 표시됨).
            var infoVer = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            // 커밋 해시(+로 붙는 SourceRevisionId)가 혹시 남아있어도 화면엔 안 보이게 방어.
            _currentVersion = infoVer?.Split('+')[0] ?? _currentVersion;
            // K-시리즈 About 창 버전 표기는 "v1.2.3" 형식으로 통일한다.
            // 예전엔 "버전 1.2.3"이었는데 K-Clock은 "v1.4.1"이라 두 앱을 나란히 놓으면
            // 표기가 달라 보였음(오푸스 시안 단계부터 앱마다 달랐던 것, 2026-08-25 통일).
            VersionText.Text = $"v{_currentVersion}";
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateBtn.IsEnabled = false;
            StatusText.Text = "확인 중...";

            var result = await UpdateChecker.CheckAsync(_currentVersion);

            if (result.Error != null)
            {
                StatusText.Text = $"업데이트 확인 실패: {result.Error} (잠시 후 다시 시도해주세요)";
                UpdateBtn.IsEnabled = true;
                return;
            }

            if (!result.HasUpdate)
            {
                StatusText.Text = "최신 버전 사용 중입니다.";
                UpdateBtn.IsEnabled = true;
                return;
            }

            StatusText.Text = "새 버전 발견, 다운로드 중...";
            try
            {
                var installerPath = await UpdateChecker.DownloadInstallerAsync(result.DownloadUrl!, result.LatestVersion!);
                StatusText.Text = "다운로드 완료! 설치를 시작합니다...";
                var psi = new ProcessStartInfo(installerPath) { UseShellExecute = true };
                // K-Zone은 다른 창을 관리하는 프로그램 특성상 형이 관리자 권한으로 실행해두는
                // 경우가 실제로 있음(2026-08-06 실측 확인 — 일반 권한 설치 프로그램은 관리자
                // 권한으로 떠있는 K-Zone을 종료시키지 못해 설치가 막힘). 지금 실행 중인 이
                // K-Zone 자신이 관리자 권한이면 설치파일도 같이 관리자 권한(runas)으로 띄워서
                // 자기 자신을 확실히 종료·교체할 수 있게 함 — 일반 권한으로 쓰는 다수 사용자는
                // 이 분기를 안 타서 평소처럼 UAC 프롬프트 없이 그대로 진행됨.
                if (IsRunningAsAdmin())
                    psi.Verb = "runas";
                Process.Start(psi);
                await System.Threading.Tasks.Task.Delay(500);
                ExitLogger.Log("UPDATE_INSTALL_SHUTDOWN", "사용자 클릭",
                    $"업데이트 설치를 위해 스스로 종료 (v{_currentVersion} → v{result.LatestVersion})");
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"다운로드 실패: {ex.Message}";
                UpdateBtn.IsEnabled = true;
            }
        }

        private static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
