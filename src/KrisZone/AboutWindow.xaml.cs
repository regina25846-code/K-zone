using System;
using System.Diagnostics;
using System.Reflection;
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
            VersionText.Text = $"버전 {_currentVersion}";
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
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                await System.Threading.Tasks.Task.Delay(500);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"다운로드 실패: {ex.Message}";
                UpdateBtn.IsEnabled = true;
            }
        }
    }
}
