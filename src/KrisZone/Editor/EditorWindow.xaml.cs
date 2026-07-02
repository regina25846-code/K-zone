using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KrisZone.Models;
using KrisZone.Settings;

namespace KrisZone.Editor
{
    public partial class EditorWindow : Window
    {
        private ZoneLayout? _currentLayout;
        private MonitorInfo? _currentMonitor;

        // Canvas drag state
        private bool _dragging;
        private Point _dragStart;
        private Rectangle? _dragRect;
        private readonly Stack<List<ZoneRect>> _undoStack = new();

        private const string NullMonitorKey = "__null__";

        public EditorWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            RefreshMonitors();
            RefreshLayoutList();

            KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
                    Undo();
            };
        }

        // ── Monitor ──────────────────────────────────────────────────────────

        private void RefreshMonitors()
        {
            MonitorManager.Refresh();
            MonitorCombo.Items.Clear();
            foreach (var m in MonitorManager.Monitors)
                MonitorCombo.Items.Add(new ComboBoxItem { Content = $"모니터: {m.Id}", Tag = m });
            if (MonitorCombo.Items.Count > 0)
                MonitorCombo.SelectedIndex = 0;
        }

        private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MonitorCombo.SelectedItem is ComboBoxItem { Tag: MonitorInfo m })
                _currentMonitor = m;
            RedrawCanvas();
        }

        // ── Layout List ───────────────────────────────────────────────────────

        private void RefreshLayoutList()
        {
            LayoutList.Items.Clear();
            foreach (var layout in SettingsManager.Current.Layouts)
                LayoutList.Items.Add(new ListBoxItem { Content = layout.Name, Tag = layout });
            if (LayoutList.Items.Count > 0)
                LayoutList.SelectedIndex = 0;
        }

        private void LayoutList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LayoutList.SelectedItem is ListBoxItem { Tag: ZoneLayout l })
            {
                _currentLayout = l;
                LayoutNameBox.Text = l.Name;
                _undoStack.Clear();
                RedrawCanvas();
            }
        }

        private void AddLayout_Click(object sender, RoutedEventArgs e)
        {
            var layout = new ZoneLayout { Name = $"레이아웃 {SettingsManager.Current.Layouts.Count + 1}" };
            SettingsManager.Current.Layouts.Add(layout);
            SettingsManager.Save();
            RefreshLayoutList();
            LayoutList.SelectedIndex = LayoutList.Items.Count - 1;
        }

        private void DeleteLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLayout == null) return;
            if (MessageBox.Show($"'{_currentLayout.Name}' 을(를) 삭제할까요?", "삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SettingsManager.Current.Layouts.Remove(_currentLayout);
            SettingsManager.Save();
            _currentLayout = null;
            RefreshLayoutList();
        }

        private void LayoutNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentLayout == null) return;
            _currentLayout.Name = LayoutNameBox.Text;
            // Update list item
            if (LayoutList.SelectedItem is ListBoxItem li) li.Content = _currentLayout.Name;
            SettingsManager.Save();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLayout == null || _currentMonitor == null) return;
            ZoneManager.AssignLayout(_currentMonitor, _currentLayout.Id);
            MessageBox.Show($"'{_currentLayout.Name}' 레이아웃이 적용되었습니다.", "적용 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Canvas ────────────────────────────────────────────────────────────

        private void RedrawCanvas()
        {
            ZoneCanvas.Children.Clear();
            if (_currentLayout == null) return;

            var s = SettingsManager.Current;

            for (int i = 0; i < _currentLayout.Zones.Count; i++)
            {
                var zone = _currentLayout.Zones[i];
                DrawZoneRect(zone, i, false);
            }
        }

        private void DrawZoneRect(ZoneRect zone, int index, bool highlight)
        {
            double cw = ZoneCanvas.ActualWidth;
            double ch = ZoneCanvas.ActualHeight;
            if (cw < 1 || ch < 1) return;

            double x = zone.X * cw;
            double y = zone.Y * ch;
            double w = zone.Width * cw;
            double h = zone.Height * ch;
            const double gap = 4;

            var rect = new Rectangle
            {
                Width = Math.Max(0, w - gap * 2),
                Height = Math.Max(0, h - gap * 2),
                Fill = new SolidColorBrush(Color.FromArgb(highlight ? 140 : 80, 0x3B, 0x82, 0xF6)),
                Stroke = new SolidColorBrush(Color.FromArgb(200, 0x60, 0xA5, 0xFA)),
                StrokeThickness = 1.5,
                RadiusX = 4, RadiusY = 4,
                Tag = index,
                Cursor = Cursors.Hand
            };
            Canvas.SetLeft(rect, x + gap);
            Canvas.SetTop(rect, y + gap);
            ZoneCanvas.Children.Add(rect);

            var tb = new TextBlock
            {
                Text = (index + 1).ToString(),
                Foreground = Brushes.White,
                FontSize = 18, FontWeight = FontWeights.Bold, Opacity = 0.8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, x + gap + 8);
            Canvas.SetTop(tb, y + gap + 8);
            ZoneCanvas.Children.Add(tb);
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentLayout == null) return;
            _dragging = true;
            _dragStart = e.GetPosition(ZoneCanvas);
            _dragRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0x3B, 0x82, 0xF6)),
                IsHitTestVisible = false
            };
            ZoneCanvas.Children.Add(_dragRect);
            ZoneCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || _dragRect == null) return;
            var pos = e.GetPosition(ZoneCanvas);
            double x = Math.Min(_dragStart.X, pos.X);
            double y = Math.Min(_dragStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);
            Canvas.SetLeft(_dragRect, x);
            Canvas.SetTop(_dragRect, y);
            _dragRect.Width = w;
            _dragRect.Height = h;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging || _currentLayout == null) return;
            _dragging = false;
            ZoneCanvas.ReleaseMouseCapture();

            var pos = e.GetPosition(ZoneCanvas);
            double x = Math.Min(_dragStart.X, pos.X);
            double y = Math.Min(_dragStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);

            if (_dragRect != null)
            {
                ZoneCanvas.Children.Remove(_dragRect);
                _dragRect = null;
            }

            if (w < 20 || h < 20) return; // Too small

            double cw = ZoneCanvas.ActualWidth;
            double ch = ZoneCanvas.ActualHeight;

            _undoStack.Push(_currentLayout.Zones.Select(z => new ZoneRect(z.X, z.Y, z.Width, z.Height)).ToList());
            _currentLayout.Zones.Add(new ZoneRect(x / cw, y / ch, w / cw, h / ch));
            SettingsManager.Save();
            RedrawCanvas();
        }

        private void Canvas_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_currentLayout == null) return;
            var pos = e.GetPosition(ZoneCanvas);
            double cw = ZoneCanvas.ActualWidth;
            double ch = ZoneCanvas.ActualHeight;
            double px = pos.X / cw, py = pos.Y / ch;

            int idx = -1;
            for (int i = _currentLayout.Zones.Count - 1; i >= 0; i--)
            {
                var z = _currentLayout.Zones[i];
                if (px >= z.X && px <= z.X + z.Width && py >= z.Y && py <= z.Y + z.Height)
                { idx = i; break; }
            }
            if (idx < 0) return;

            _undoStack.Push(_currentLayout.Zones.Select(z => new ZoneRect(z.X, z.Y, z.Width, z.Height)).ToList());
            _currentLayout.Zones.RemoveAt(idx);
            SettingsManager.Save();
            RedrawCanvas();
        }

        private void Undo()
        {
            if (_currentLayout == null || _undoStack.Count == 0) return;
            _currentLayout.Zones = _undoStack.Pop();
            SettingsManager.Save();
            RedrawCanvas();
        }

        // ── Settings Panel ────────────────────────────────────────────────────

        private bool _loadingSettings;

        private void LoadSettings()
        {
            _loadingSettings = true;
            var s = SettingsManager.Current;
            ZoneColorBox.Text = s.ZoneColor;
            HighlightColorBox.Text = s.ZoneHighlightColor;
            OpacitySlider.Value = s.ZoneHighlightOpacity;
            OpacityLabel.Text = $"{s.ZoneHighlightOpacity}%";
            ShiftDragCheck.IsChecked = s.ShiftDrag;
            TransparentCheck.IsChecked = s.MakeDraggedWindowTransparent;
            ShowNumberCheck.IsChecked = s.ShowZoneNumber;
            OverrideSnapCheck.IsChecked = s.OverrideSnapHotkeys;
            AppLastZoneCheck.IsChecked = s.AppLastZone;
            ExcludedAppsBox.Text = string.Join("\n", s.ExcludedApps);
            _loadingSettings = false;
        }

        private void ZoneColor_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loadingSettings) return;
            SettingsManager.Current.ZoneColor = ZoneColorBox.Text;
            SettingsManager.Current.ZoneHighlightColor = HighlightColorBox.Text;
            SettingsManager.Save();
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingSettings) return;
            int v = (int)OpacitySlider.Value;
            OpacityLabel.Text = $"{v}%";
            SettingsManager.Current.ZoneHighlightOpacity = v;
            SettingsManager.Save();
        }

        private void Settings_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            var s = SettingsManager.Current;
            s.ShiftDrag = ShiftDragCheck.IsChecked == true;
            s.MakeDraggedWindowTransparent = TransparentCheck.IsChecked == true;
            s.ShowZoneNumber = ShowNumberCheck.IsChecked == true;
            s.OverrideSnapHotkeys = OverrideSnapCheck.IsChecked == true;
            s.AppLastZone = AppLastZoneCheck.IsChecked == true;
            SettingsManager.Save();
        }

        private void ExcludedApps_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loadingSettings) return;
            SettingsManager.Current.ExcludedApps = ExcludedAppsBox.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            SettingsManager.Save();
        }
    }
}
