using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            AppLastZoneCheck.IsChecked  = s.AppLastZone;
            ExcludedAppsBox.Text = string.Join("\n", s.ExcludedApps);
            _loading = false;
        }

        private void ZoneColor_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            SettingsManager.Current.ZoneColor = ZoneColorBox.Text;
            SettingsManager.Current.ZoneHighlightColor = HighlightColorBox.Text;
            SettingsManager.Save();
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
            s.AppLastZone                 = AppLastZoneCheck.IsChecked == true;
            SettingsManager.Save();
        }

        private void ExcludedApps_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            SettingsManager.Current.ExcludedApps = ExcludedAppsBox.Text
                .Split('\n', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            SettingsManager.Save();
        }
    }
}
