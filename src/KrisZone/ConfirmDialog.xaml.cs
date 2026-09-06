using System.Windows;

namespace KrisZone
{
    /// <summary>
    /// K-Zone 공용 확인창. 되돌릴 수 없는 동작 앞에 한 번 물어보는 자리다.
    ///
    /// 이 앱에는 확인창이 지금까지 하나도 없었고(MessageBox 소스 전체 0건),
    /// 윈도우 기본 흰 상자를 쓰지 않는다는 방침이라 앱 디자인으로 직접 만들었다.
    /// 앞으로 생길 다른 확인창도 새 창을 또 만들지 말고 이 <see cref="Ask"/>를 재사용한다.
    ///
    /// 안전 원칙: 실수했을 때 손해가 큰 쪽(확인 버튼)은 절대 기본이 되지 않는다.
    /// Enter(IsDefault)·Esc(IsCancel)·최초 키보드 포커스가 모두 취소 버튼에 걸려 있어서,
    /// 창이 뜨자마자 Enter나 Esc를 무심코 눌러도 취소로만 닫힌다.
    /// 창을 Alt+F4로 닫거나 강제로 닫아도 DialogResult가 null로 남아 취소로 취급된다.
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        private ConfirmDialog(string title, string message, string confirmText, string cancelText)
        {
            InitializeComponent();

            TitleText.Text    = title;
            MessageText.Text  = message;
            ConfirmLabel.Text = confirmText;
            CancelLabel.Text  = cancelText;

            // XAML의 FocusManager 대신 Loaded에서 직접 준다 — 창이 실제로 활성화된 뒤에
            // 줘야 키보드 포커스(IsKeyboardFocused, 포커스 링)가 확실히 취소 버튼에 앉는다.
            Loaded += (_, _) => CancelButton.Focus();
        }

        /// <summary>
        /// 확인창을 띄우고, 사용자가 확인 버튼을 눌렀을 때만 true를 돌려준다.
        /// 취소·Esc·창 닫기는 전부 false다.
        /// </summary>
        /// <param name="owner">이 창을 띄운 창(가운데 정렬 기준). null이면 화면 가운데.</param>
        public static bool Ask(Window? owner, string title, string message,
                               string confirmText, string cancelText)
        {
            var dialog = new ConfirmDialog(title, message, confirmText, cancelText);

            // Owner는 이미 보이는 창만 지정할 수 있다(아직 안 뜬 창을 넣으면 예외).
            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return dialog.ShowDialog() == true;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;   // 대입하는 순간 창이 닫힌다
        }

        // 취소 버튼에는 Click 핸들러를 달지 않는다. IsCancel="True"가 이미
        // DialogResult=false로 닫아주기 때문에, 여기서 또 대입하면 이미 닫힌 창에
        // 두 번 대입하는 모양이 된다.
    }
}
