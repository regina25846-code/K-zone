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
    public partial class MonitorOverlayEditor : Window
    {
        private readonly MonitorInfo _monitor;
        private ZoneLayout? _currentLayout;

        private bool _dragging;
        private Point _dragStart;
        private Rectangle? _dragRect;
        private readonly Stack<List<ZoneRect>> _undoStack = new();

        private bool _loadingLayout;

        public MonitorOverlayEditor(MonitorInfo monitor)
        {
            _monitor = monitor;
            InitializeComponent();

            // Position this window exactly over the target monitor (physical coords)
            var wa = monitor.WorkArea;
            double scale = monitor.ScaleFactor;
            Left   = wa.X;
            Top    = wa.Y;
            Width  = wa.Width;
            Height = wa.Height;

            MonitorLabel.Text = monitor.DisplayName + "  |";

            Loaded  += OnLoaded;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo();
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshLayoutCombo();
        }

        // ── Layout list ───────────────────────────────────────────────────────

        private void RefreshLayoutCombo()
        {
            _loadingLayout = true;
            LayoutCombo.Items.Clear();
            foreach (var l in SettingsManager.Current.Layouts)
                LayoutCombo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l });

            // Select currently applied layout for this monitor
            var cfg = SettingsManager.Current.MonitorConfigs.FirstOrDefault(c => c.MonitorId == _monitor.Id);
            if (cfg != null)
            {
                foreach (ComboBoxItem item in LayoutCombo.Items)
                    if (item.Tag is ZoneLayout l && l.Id == cfg.LayoutId)
                    { LayoutCombo.SelectedItem = item; break; }
            }
            if (LayoutCombo.SelectedIndex < 0 && LayoutCombo.Items.Count > 0)
                LayoutCombo.SelectedIndex = 0;

            _loadingLayout = false;
            SelectCurrentLayout();
        }

        private void SelectCurrentLayout()
        {
            if (LayoutCombo.SelectedItem is ComboBoxItem { Tag: ZoneLayout l })
            {
                _currentLayout = l;
                LayoutNameBox.Text = l.Name;
                _undoStack.Clear();
            }
            RedrawCanvas();
        }

        private void LayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingLayout) return;
            SelectCurrentLayout();
        }

        private void LayoutNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentLayout == null || _loadingLayout) return;
            _currentLayout.Name = LayoutNameBox.Text;
            if (LayoutCombo.SelectedItem is ComboBoxItem ci) ci.Content = _currentLayout.Name;
            SettingsManager.Save();
        }

        private void AddLayout_Click(object sender, RoutedEventArgs e)
        {
            var layout = new ZoneLayout { Name = $"레이아웃 {SettingsManager.Current.Layouts.Count + 1}" };
            SettingsManager.Current.Layouts.Add(layout);
            SettingsManager.Save();
            RefreshLayoutCombo();
            LayoutCombo.SelectedIndex = LayoutCombo.Items.Count - 1;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLayout == null) return;
            ZoneManager.AssignLayout(_monitor, _currentLayout.Id);
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ── Canvas drawing ────────────────────────────────────────────────────

        private void RedrawCanvas()
        {
            ZoneCanvas.Children.Clear();
            if (_currentLayout == null) return;

            for (int i = 0; i < _currentLayout.Zones.Count; i++)
                DrawZone(_currentLayout.Zones[i], i, false);
        }

        private void DrawZone(ZoneRect zone, int index, bool highlight)
        {
            double cw = ZoneCanvas.ActualWidth;
            double ch = ZoneCanvas.ActualHeight;
            if (cw < 1 || ch < 1) return;

            double x = zone.X * cw, y = zone.Y * ch;
            double w = zone.Width * cw, h = zone.Height * ch;
            const double gap = 4;

            var rect = new Rectangle
            {
                Width  = Math.Max(0, w - gap * 2),
                Height = Math.Max(0, h - gap * 2),
                Fill   = new SolidColorBrush(Color.FromArgb(highlight ? (byte)160 : (byte)100, 0x3B, 0x82, 0xF6)),
                Stroke = new SolidColorBrush(Color.FromArgb(220, 0x60, 0xA5, 0xFA)),
                StrokeThickness = 2,
                RadiusX = 6, RadiusY = 6,
                Tag = index, Cursor = Cursors.Hand
            };
            Canvas.SetLeft(rect, x + gap);
            Canvas.SetTop(rect, y + gap);
            ZoneCanvas.Children.Add(rect);

            var tb = new TextBlock
            {
                Text = (index + 1).ToString(),
                Foreground = Brushes.White,
                FontSize = 28, FontWeight = FontWeights.Bold, Opacity = 0.7,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(tb, x + gap + 12);
            Canvas.SetTop(tb, y + gap + 12);
            ZoneCanvas.Children.Add(tb);
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentLayout == null || e.ClickCount != 1) return;
            _dragging = true;
            _dragStart = e.GetPosition(ZoneCanvas);
            _dragRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(50, 0x3B, 0x82, 0xF6)),
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
            Canvas.SetLeft(_dragRect, x);
            Canvas.SetTop(_dragRect, y);
            _dragRect.Width  = Math.Abs(pos.X - _dragStart.X);
            _dragRect.Height = Math.Abs(pos.Y - _dragStart.Y);
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

            if (_dragRect != null) { ZoneCanvas.Children.Remove(_dragRect); _dragRect = null; }
            if (w < 30 || h < 30) return;

            double cw = ZoneCanvas.ActualWidth, ch = ZoneCanvas.ActualHeight;
            _undoStack.Push(_currentLayout.Zones.Select(z => new ZoneRect(z.X, z.Y, z.Width, z.Height)).ToList());
            _currentLayout.Zones.Add(new ZoneRect(x / cw, y / ch, w / cw, h / ch));
            SettingsManager.Save();
            RedrawCanvas();
        }

        private void Canvas_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_currentLayout == null) return;
            var pos = e.GetPosition(ZoneCanvas);
            double cw = ZoneCanvas.ActualWidth, ch = ZoneCanvas.ActualHeight;
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
    }
}
