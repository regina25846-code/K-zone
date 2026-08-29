using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KrisZone
{
    // 핀 고정된 창을 감싸는 클릭 통과 테두리. ZoneOverlay와 달리 드래그 중에만 잠깐 뜨는 게
    // 아니라 핀이 풀릴 때까지 계속 떠있어야 해서, OS 레벨로 확실히 클릭 통과되게
    // WS_EX_TRANSPARENT를 걸어준다(WPF의 IsHitTestVisible만으론 부족함).
    public partial class PinBorderOverlay : Window
    {
        public PinBorderOverlay()
        {
            InitializeComponent();
            // 파워토이즈 AlwaysOnTop의 기본 테두리 두께(DefaultFrameThickness = 4).
            BorderRect.BorderThickness = new Thickness(AccentColor.FrameThickness);
            SetBorderColor(AccentColor.Current());

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                    style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW);
            };
        }

        // 사용자가 윈도우 테마 강조색을 바꾸면 이미 핀 고정된 창의 테두리도 즉시 따라가야 한다
        // (파워토이즈가 ColorValuesChanged에서 다시 그리는 것과 같은 동작).
        internal void SetBorderColor(Color color)
        {
            BorderRect.BorderBrush = new SolidColorBrush(color);
        }

        internal void UpdateRect(NativeMethods.RECT r, double scale)
        {
            Left = r.Left / scale;
            Top = r.Top / scale;
            Width = Math.Max(0, (r.Right - r.Left) / scale);
            Height = Math.Max(0, (r.Bottom - r.Top) / scale);
        }
    }
}
