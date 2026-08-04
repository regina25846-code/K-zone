using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace KrisZone
{
    public partial class AboutWindow : Window
    {
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
            VersionText.Text = $"버전 {infoVer?.Split('+')[0]}";
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://regina25846-code.github.io/K-zone/") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
