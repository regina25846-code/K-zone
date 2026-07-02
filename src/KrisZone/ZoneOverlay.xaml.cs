using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KrisZone.Models;
using KrisZone.Settings;

namespace KrisZone
{
    public partial class ZoneOverlay : Window
    {
        private MonitorInfo? _monitor;
        private ZoneLayout? _layout;

        public ZoneOverlay()
        {
            InitializeComponent();
        }

        public void Show(MonitorInfo monitor, ZoneLayout layout, List<int> highlighted)
        {
            _monitor = monitor;
            _layout = layout;

            var bounds = monitor.Bounds;
            Left = bounds.X;
            Top = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;

            DrawZones(highlighted);
            Show();
        }

        public void UpdateHighlight(List<int> highlighted)
        {
            DrawZones(highlighted);
        }

        private void DrawZones(List<int> highlighted)
        {
            if (_monitor == null || _layout == null) return;
            ZoneCanvas.Children.Clear();

            var s = SettingsManager.Current;
            var normalBrush = ParseColor(s.ZoneColor, s.ZoneHighlightOpacity);
            var highlightBrush = ParseColor(s.ZoneHighlightColor, Math.Min(255, s.ZoneHighlightOpacity + 80));
            var borderBrush = new SolidColorBrush(ParseColorSolid(s.ZoneBorderColor));
            var numberBrush = new SolidColorBrush(ParseColorSolid(s.ZoneNumberColor));

            var wa = _monitor.WorkArea;
            // Overlay is positioned at monitor.Bounds, so offset by workarea delta
            double offsetX = wa.X - _monitor.Bounds.X;
            double offsetY = wa.Y - _monitor.Bounds.Y;

            for (int i = 0; i < _layout.Zones.Count; i++)
            {
                var zone = _layout.Zones[i];
                bool isHit = highlighted.Contains(i);

                double x = offsetX + zone.X * wa.Width;
                double y = offsetY + zone.Y * wa.Height;
                double w = zone.Width * wa.Width;
                double h = zone.Height * wa.Height;

                const double gap = 4;
                var rect = new Rectangle
                {
                    Width = Math.Max(0, w - gap * 2),
                    Height = Math.Max(0, h - gap * 2),
                    Fill = isHit ? highlightBrush : normalBrush,
                    Stroke = borderBrush,
                    StrokeThickness = 1,
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(rect, x + gap);
                Canvas.SetTop(rect, y + gap);
                ZoneCanvas.Children.Add(rect);

                if (s.ShowZoneNumber)
                {
                    var tb = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        Foreground = numberBrush,
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Opacity = 0.8
                    };
                    Canvas.SetLeft(tb, x + gap + 8);
                    Canvas.SetTop(tb, y + gap + 8);
                    ZoneCanvas.Children.Add(tb);
                }
            }
        }

        private static SolidColorBrush ParseColor(string hex, int opacity)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                c.A = (byte)Math.Clamp(opacity * 255 / 100, 0, 255);
                return new SolidColorBrush(c);
            }
            catch { return new SolidColorBrush(Color.FromArgb(128, 0x00, 0x8C, 0xFF)); }
        }

        private static Color ParseColorSolid(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.White; }
        }
    }
}
