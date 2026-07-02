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
    public partial class LayoutBrowserWindow : Window
    {
        private MonitorInfo? _selectedMonitor;
        private readonly List<Border> _monitorTabs = new();

        public LayoutBrowserWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildMonitorTabs();
            BuildLayoutCards();
        }

        // ── 모니터 탭 ─────────────────────────────────────────────────────────

        private void BuildMonitorTabs()
        {
            MonitorPanel.Children.Clear();
            _monitorTabs.Clear();

            var monitors = MonitorManager.Monitors;
            _selectedMonitor = monitors.Count > 0 ? monitors[0] : null;

            for (int i = 0; i < monitors.Count; i++)
            {
                int localI = i;
                var tab = CreateMonitorTab(monitors[i], i + 1, i == 0);
                _monitorTabs.Add(tab);
                MonitorPanel.Children.Add(tab);
                tab.MouseLeftButtonDown += (_, _) => SelectMonitor(localI);
            }
        }

        private Border CreateMonitorTab(MonitorInfo monitor, int idx, bool selected)
        {
            // 실제 모니터 비율에 맞게 탭 너비 계산 (16:9 기준 120px)
            const double baseWidth = 120;
            const double baseAspect = 16.0 / 9.0;
            double aspect = (double)monitor.WorkArea.Width / monitor.WorkArea.Height;
            double tabWidth = Math.Clamp(baseWidth * aspect / baseAspect, 80, 280);

            var blue = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            var dark = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
            var gray = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

            var border = new Border
            {
                Width = tabWidth, Padding = new Thickness(10, 10, 10, 10),
                Background = Brushes.White,
                BorderBrush = selected
                    ? blue
                    : new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
            };

            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = idx.ToString(), FontSize = 32, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = selected ? blue : dark,
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{monitor.WorkArea.Width} × {monitor.WorkArea.Height}",
                FontSize = 12, Foreground = gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            sp.Children.Add(new TextBlock
            {
                Text = "100%", FontSize = 12,
                Foreground = selected ? blue : gray,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            border.Child = sp;
            return border;
        }

        private void SelectMonitor(int index)
        {
            var monitors = MonitorManager.Monitors;
            if (index >= monitors.Count) return;
            _selectedMonitor = monitors[index];

            var blue     = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            var dark     = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
            var gray     = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
            var lineGray = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

            for (int i = 0; i < _monitorTabs.Count; i++)
            {
                bool sel = i == index;
                var tab = _monitorTabs[i];
                tab.BorderBrush = sel ? blue : lineGray;
                tab.BorderThickness = new Thickness(sel ? 2 : 1);
                var sp = (StackPanel)tab.Child;
                ((TextBlock)sp.Children[0]).Foreground = sel ? blue : dark;
                ((TextBlock)sp.Children[2]).Foreground = sel ? blue : gray;
            }

            BuildLayoutCards();
        }

        // ── 레이아웃 카드 ─────────────────────────────────────────────────────

        private void BuildLayoutCards()
        {
            ContentPanel.Children.Clear();

            var cfg = _selectedMonitor != null
                ? SettingsManager.Current.MonitorConfigs.FirstOrDefault(c => c.MonitorId == _selectedMonitor.Id)
                : null;

            var templates = SettingsManager.Current.Layouts.Where(l => l.IsTemplate).ToList();
            var customs   = SettingsManager.Current.Layouts.Where(l => !l.IsTemplate).ToList();

            if (templates.Count > 0)
            {
                ContentPanel.Children.Add(SectionHeader("템플릿"));
                var panel = CardWrapPanel();
                foreach (var l in templates)
                    panel.Children.Add(CreateLayoutCard(l, cfg?.LayoutId == l.Id));
                ContentPanel.Children.Add(panel);
            }

            ContentPanel.Children.Add(SectionHeader("사용자 지정", templates.Count > 0 ? 24 : 0));
            var customPanel = CardWrapPanel();
            foreach (var l in customs)
                customPanel.Children.Add(CreateLayoutCard(l, cfg?.LayoutId == l.Id));
            ContentPanel.Children.Add(customPanel);
        }

        private static TextBlock SectionHeader(string text, double topMargin = 0) => new TextBlock
        {
            Text = text, FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            Margin = new Thickness(0, topMargin, 0, 16),
        };

        private static WrapPanel CardWrapPanel() => new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };

        private Border CreateLayoutCard(ZoneLayout layout, bool selected)
        {
            var blue    = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            var lineGray = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            var hoverLine = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE));

            var card = new Border
            {
                Width = 210, Height = 175,
                Background = selected
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF))
                    : Brushes.White,
                BorderBrush = selected ? blue : lineGray,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 12, 12),
                Cursor = Cursors.Hand,
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 헤더: 이름 + 연필
            var header = new Grid { Margin = new Thickness(12, 12, 12, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = layout.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var editBtn = new Button
            {
                Content = "✏", Cursor = Cursors.Hand,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 0, 0),
                ToolTip = "레이아웃 편집",
            };

            Grid.SetColumn(nameText, 0);
            Grid.SetColumn(editBtn, 1);
            header.Children.Add(nameText);
            header.Children.Add(editBtn);
            Grid.SetRow(header, 0);

            // 미리보기
            var preview = CreatePreviewElement(layout);
            Grid.SetRow(preview, 1);

            outer.Children.Add(header);
            outer.Children.Add(preview);
            card.Child = outer;

            // 이벤트
            var layoutRef = layout;
            editBtn.Click += (s, e) => { e.Handled = true; OpenEditorForLayout(layoutRef); };
            card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ApplyLayout(layoutRef); };
            if (!selected)
            {
                card.MouseEnter += (_, _) => card.BorderBrush = hoverLine;
                card.MouseLeave += (_, _) => card.BorderBrush = lineGray;
            }

            return card;
        }

        private UIElement CreatePreviewElement(ZoneLayout layout)
        {
            var bg = new Border
            {
                Margin = new Thickness(12, 8, 12, 12),
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                CornerRadius = new CornerRadius(4),
            };
            var canvas = new Canvas();
            bg.Child = canvas;

            canvas.SizeChanged += (_, e) =>
            {
                canvas.Children.Clear();
                double w = e.NewSize.Width, h = e.NewSize.Height;
                if (w < 1 || h < 1 || layout.Zones.Count == 0) return;
                const double gap = 2;
                foreach (var z in layout.Zones)
                {
                    var rect = new Rectangle
                    {
                        Width  = Math.Max(1, z.Width  * w - gap * 2),
                        Height = Math.Max(1, z.Height * h - gap * 2),
                        Fill   = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                        RadiusX = 2, RadiusY = 2,
                    };
                    Canvas.SetLeft(rect, z.X * w + gap);
                    Canvas.SetTop(rect,  z.Y * h + gap);
                    canvas.Children.Add(rect);
                }
            };

            return bg;
        }

        // ── 액션 ──────────────────────────────────────────────────────────────

        private void ApplyLayout(ZoneLayout layout)
        {
            if (_selectedMonitor == null) return;
            ZoneManager.AssignLayout(_selectedMonitor, layout.Id);
            BuildLayoutCards();
        }

        private void OpenEditorForLayout(ZoneLayout layout)
        {
            if (_selectedMonitor == null) return;
            var editor = new MonitorOverlayEditor(_selectedMonitor, layout.Id);
            editor.Show();
            editor.Activate();
        }

        private void NewLayout_Click(object sender, RoutedEventArgs e)
        {
            var layout = new ZoneLayout
            {
                Name = $"레이아웃 {SettingsManager.Current.Layouts.Count + 1}",
                Grid = GridMeta.Default1x1(),
                IsTemplate = false,
            };
            SettingsManager.Current.Layouts.Add(layout);
            SettingsManager.Save();
            BuildLayoutCards();
            OpenEditorForLayout(layout);
        }
    }
}
