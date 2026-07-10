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
        public PinBorderOverlay(string colorHex)
        {
            InitializeComponent();
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                BorderRect.BorderBrush = new SolidColorBrush(color);
            }
            catch { BorderRect.BorderBrush = new SolidColorBrush(Colors.DeepSkyBlue); }

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                    style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW);
            };
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
