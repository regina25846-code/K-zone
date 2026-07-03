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
        private int _selectedMonitorIndex = 0;

        private static readonly Color AccentColor  = Color.FromRgb(0x00, 0x78, 0xD4);
        private static readonly Color DarkColor    = Color.FromRgb(0x11, 0x18, 0x27);
        private static readonly Color GrayColor    = Color.FromRgb(0x6B, 0x72, 0x80);
        private static readonly Color LineGray     = Color.FromRgb(0xE5, 0xE7, 0xEB);
        private static readonly Color AccentBg     = Color.FromRgb(0xE5, 0xF2, 0xFB);

        private const string FontBold   = "KoPubWorld Dotum";
        private const string FontMedium = "KoPubWorld Dotum";

        public LayoutBrowserWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 가장 큰 모니터(면적 기준) 기본 선택
            var monitors = MonitorManager.Monitors.OrderBy(m => m.Bounds.X).ToList();
            _selectedMonitorIndex = monitors
                .Select((m, i) => (area: m.Bounds.Width * m.Bounds.Height, idx: i))
                .OrderByDescending(x => x.area)
                .FirstOrDefault().idx;
            BuildMonitorTabs(_selectedMonitorIndex);
            BuildLayoutCards();
        }

        // ── 모니터 탭 ─────────────────────────────────────────────────────────

        private void BuildMonitorTabs(int selectedIndex = 0)
        {
            MonitorPanel.Children.Clear();
            var monitors = MonitorManager.Monitors.OrderBy(m => m.Bounds.X).ToList();
            _selectedMonitor = monitors.Count > selectedIndex ? monitors[selectedIndex] : null;

            double maxArea = monitors.Count > 0
                ? monitors.Max(m => m.Bounds.Width * m.Bounds.Height)
                : 0;

            for (int i = 0; i < monitors.Count; i++)
            {
                int localI = i;
                bool isLargest = monitors[i].Bounds.Width * monitors[i].Bounds.Height >= maxArea;
                var tab = CreateMonitorTab(monitors[i], i + 1, i == selectedIndex, isLargest);
                MonitorPanel.Children.Add(tab);
                tab.MouseLeftButtonDown += (_, _) =>
                {
                    _selectedMonitorIndex = localI;
                    BuildMonitorTabs(localI);
                    BuildLayoutCards();
                };
            }
        }

        private Border CreateMonitorTab(MonitorInfo monitor, int idx, bool selected, bool isLargest = false)
        {
            double monW = monitor.Bounds.Width;
            double monH = monitor.Bounds.Height;

            // 탭 너비: 16:9 기준 160px, 비율에 맞게 (탭 자체가 모니터 비율)
            const double baseWidth = 160;
            const double baseAspect = 16.0 / 9.0;
            double aspect = monW / monH;
            double tabWidth = Math.Clamp(baseWidth * aspect / baseAspect, 60, 380);

            var accent   = new SolidColorBrush(AccentColor);
            var dark     = new SolidColorBrush(DarkColor);
            var gray     = new SolidColorBrush(GrayColor);
            var lineGray = new SolidColorBrush(LineGray);

            var sp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            sp.Children.Add(new TextBlock
            {
                Text = idx.ToString(), FontSize = 32, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily(FontBold),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = selected ? accent : dark,
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{(int)monW} × {(int)monH}",
                FontSize = 13, FontFamily = new FontFamily(FontMedium),
                Foreground = gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            sp.Children.Add(new TextBlock
            {
                Text = "100%", FontSize = 13,
                FontFamily = new FontFamily(FontMedium),
                Foreground = selected ? accent : gray,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            if (isLargest)
            {
                sp.Children.Add(new Border
                {
                    Background = accent,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 5, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "가장 큼", FontSize = 10, FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                    },
                });
            }

            return new Border
            {
                Width = tabWidth, MinHeight = 100,
                Padding = new Thickness(10, 14, 10, 14),
                Background = selected ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF8, 0xFF)) : Brushes.White,
                BorderBrush = selected ? accent : lineGray,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Child = sp,
            };
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
            Text = text, FontSize = 24, FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily(FontBold),
            Foreground = new SolidColorBrush(DarkColor),
            Margin = new Thickness(0, topMargin, 0, 16),
        };

        private static WrapPanel CardWrapPanel() => new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };

        private Border CreateLayoutCard(ZoneLayout layout, bool selected)
        {
            var accent    = new SolidColorBrush(AccentColor);
            var lineGray  = new SolidColorBrush(LineGray);
            var hoverLine = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD));

            var card = new Border
            {
                Width = 185, Height = 175,
                Background = selected
                    ? new SolidColorBrush(AccentBg)
                    : Brushes.White,
                BorderBrush = selected ? accent : lineGray,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 12, 12),
                Cursor = Cursors.Hand,
            };

            var outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(12, 12, 12, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = layout.Name, FontSize = 15, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily(FontMedium),
                Foreground = new SolidColorBrush(DarkColor),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var editBtn = new Button
            {
                Content = "✏", Cursor = Cursors.Hand,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                FontSize = 15, Foreground = new SolidColorBrush(GrayColor),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 0, 0),
                ToolTip = "레이아웃 편집",
            };

            Grid.SetColumn(nameText, 0);
            Grid.SetColumn(editBtn, 1);
            header.Children.Add(nameText);
            header.Children.Add(editBtn);
            Grid.SetRow(header, 0);

            var preview = CreatePreviewElement(layout);
            Grid.SetRow(preview, 1);

            outer.Children.Add(header);
            outer.Children.Add(preview);
            card.Child = outer;

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
                CornerRadius = new CornerRadius(3),
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
            Close();
        }

        private void OpenEditorForLayout(ZoneLayout layout)
        {
            if (_selectedMonitor == null) return;
            var editor = new MonitorOverlayEditor(_selectedMonitor, layout.Id);
            editor.Closed += (_, _) => BuildLayoutCards();
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
