using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KrisZone.Settings;

namespace KrisZone.Editor
{
    public partial class SettingsWindow : Window
    {
        private bool _loading = true;

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            var s = SettingsManager.Current;
            ZoneColorBox.Text        = s.ZoneColor;
            HighlightColorBox.Text   = s.ZoneHighlightColor;
            OpacitySlider.Value      = s.ZoneHighlightOpacity;
            OpacityLabel.Text        = $"{s.ZoneHighlightOpacity}%";
            ShiftDragCheck.IsChecked = s.ShiftDrag;
            TransparentCheck.IsChecked  = s.MakeDraggedWindowTransparent;
            ShowNumberCheck.IsChecked   = s.ShowZoneNumber;
            OverrideSnapCheck.IsChecked = s.OverrideSnapHotkeys;
            AlwaysOnTopCheck.IsChecked  = s.AlwaysOnTopEnabled;
            AlwaysOnTopColorBox.Text    = s.AlwaysOnTopBorderColor;
            _loading = false;
            UpdateAllPreviews();
        }

        private void ZoneColor_Changed(object sender, TextChangedEventArgs e)
        {
            UpdatePreview(ZoneColorBox.Text, ZoneColorPreview);
            UpdatePreview(HighlightColorBox.Text, HighlightColorPreview);
            UpdatePreview(AlwaysOnTopColorBox.Text, AlwaysOnTopColorPreview);

            if (_loading) return;
            SettingsManager.Current.ZoneColor = ZoneColorBox.Text;
            SettingsManager.Current.ZoneHighlightColor = HighlightColorBox.Text;
            SettingsManager.Current.AlwaysOnTopBorderColor = AlwaysOnTopColorBox.Text;
            SettingsManager.Save();
        }

        private void UpdateAllPreviews()
        {
            UpdatePreview(ZoneColorBox.Text, ZoneColorPreview);
            UpdatePreview(HighlightColorBox.Text, HighlightColorPreview);
            UpdatePreview(AlwaysOnTopColorBox.Text, AlwaysOnTopColorPreview);
        }

        private static void UpdatePreview(string hex, Border preview)
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { preview.Background = System.Windows.Media.Brushes.Transparent; }
        }

        private void Opacity_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || OpacityLabel == null) return;
            int v = (int)OpacitySlider.Value;
            OpacityLabel.Text = $"{v}%";
            SettingsManager.Current.ZoneHighlightOpacity = v;
            SettingsManager.Save();
        }

        private void Settings_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = SettingsManager.Current;
            s.ShiftDrag                   = ShiftDragCheck.IsChecked == true;
            s.MakeDraggedWindowTransparent = TransparentCheck.IsChecked == true;
            s.ShowZoneNumber              = ShowNumberCheck.IsChecked == true;
            s.OverrideSnapHotkeys         = OverrideSnapCheck.IsChecked == true;
            s.AlwaysOnTopEnabled           = AlwaysOnTopCheck.IsChecked == true;
            SettingsManager.Save();
        }

        // 화면 아무 곳이나 클릭하면 그 픽셀 색을 뽑아서 해당 텍스트박스에 채워넣는 스포이드 도구
        private void EyeDropper_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string targetName) return;

            var overlay = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Topmost = true,
                ShowInTaskbar = false,
                Cursor = Cursors.Cross,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
            };

            overlay.PreviewMouseLeftButtonDown += (_, _) =>
            {
                var pt = System.Windows.Forms.Cursor.Position;
                using var bmp = new System.Drawing.Bitmap(1, 1);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(pt.X, pt.Y, 0, 0, new System.Drawing.Size(1, 1));
                var c = bmp.GetPixel(0, 0);
                string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

                switch (targetName)
                {
                    case "ZoneColorBox": ZoneColorBox.Text = hex; break;
                    case "HighlightColorBox": HighlightColorBox.Text = hex; break;
                    case "AlwaysOnTopColorBox": AlwaysOnTopColorBox.Text = hex; break;
                }
                overlay.Close();
            };
            overlay.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) overlay.Close(); };

            overlay.Show();
            overlay.Activate();
        }
    }
}
