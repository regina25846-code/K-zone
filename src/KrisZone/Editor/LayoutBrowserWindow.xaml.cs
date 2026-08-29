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
        private Border? _activeCard;

        private static readonly Color AccentColor  = Color.FromRgb(0x2E, 0x42, 0x72);
        // 눌림 상태용 더 진한 네이비 — 이 앱이 이미 쓰는 토큰(새 레이아웃 버튼 호버,
        // 안내패널 저장 버튼 호버, 합치기 버튼 그림자와 같은 값).
        private static readonly Color AccentDeep   = Color.FromRgb(0x1E, 0x2E, 0x54);
        private static readonly Color DarkColor    = Color.FromRgb(0x22, 0x25, 0x2B);
        private static readonly Color GrayColor    = Color.FromRgb(0x56, 0x5B, 0x65);
        private static readonly Color LineGray     = Color.FromRgb(0xDE, 0xD9, 0xD1);
        private static readonly Color AccentBg     = Color.FromRgb(0xEA, 0xEC, 0xF3);

        private static readonly FontFamily UiFont = UiFonts.Pretendard;

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

            for (int i = 0; i < monitors.Count; i++)
            {
                int localI = i;
                var tab = CreateMonitorTab(monitors[i], i + 1, i == selectedIndex);
                MonitorPanel.Children.Add(tab);
                tab.MouseLeftButtonDown += (_, _) =>
                {
                    _selectedMonitorIndex = localI;
                    BuildMonitorTabs(localI);
                    BuildLayoutCards();
                };
            }
        }

        private Border CreateMonitorTab(MonitorInfo monitor, int idx, bool selected)
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
                FontFamily = UiFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = selected ? accent : dark,
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{(int)monW} × {(int)monH}",
                FontSize = 13, FontFamily = UiFont,
                Foreground = gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            sp.Children.Add(new TextBlock
            {
                Text = "100%", FontSize = 13,
                FontFamily = UiFont,
                Foreground = selected ? accent : gray,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            return new Border
            {
                Width = tabWidth, MinHeight = 100,
                Padding = new Thickness(10, 14, 10, 14),
                Background = selected ? new SolidColorBrush(AccentBg) : Brushes.White,
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
            FontFamily = UiFont,
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
            var hoverLine = new SolidColorBrush(Color.FromRgb(0x8C, 0x9C, 0xC2));

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
                FontFamily = UiFont,
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
                // ⚠ Background=Transparent만으로는 호버 하이라이트가 안 없어진다. WPF 기본 Button
                //   템플릿이 자체 IsMouseOver 트리거로 하늘색 그라디언트를 칠하기 때문에, 우리가
                //   준 Background은 그때 무시된다. 기본 크롬이 아예 없는 템플릿으로 교체해야 한다.
                Template = BuildFlatIconButtonTemplate(),
            };
            // 호버·눌림 표현은 BuildFlatIconButtonTemplate() 안 트리거가 전담한다
            // (배경 박스 없이 글리프 색만 바뀌는 방식 — About 창 × 버튼과 같은 언어).

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

            if (selected) _activeCard = card;

            var layoutRef = layout;
            editBtn.Click += (s, e) => { e.Handled = true; OpenEditorForLayout(layoutRef); };
            // 한 번 클릭하면 적용은 안 하고 선택 표시(테두리/배경)만 그 카드로 옮긴다 —
            // 이전엔 더블클릭으로 실제 적용될 때만 표시가 바뀌어서, 한 번 클릭해도 선택
            // 사각박스가 그대로였다(2026-08-04 형이 실제 재현). 더블클릭은 그대로 적용까지 함.
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { ApplyLayout(layoutRef); return; }
                if (_activeCard == card) return;
                if (_activeCard != null)
                {
                    _activeCard.Background = Brushes.White;
                    _activeCard.BorderBrush = lineGray;
                    _activeCard.BorderThickness = new Thickness(1);
                }
                card.Background = new SolidColorBrush(AccentBg);
                card.BorderBrush = accent;
                card.BorderThickness = new Thickness(2);
                _activeCard = card;
            };
            card.MouseEnter += (_, _) => { if (_activeCard != card) card.BorderBrush = hoverLine; };
            card.MouseLeave += (_, _) => { if (_activeCard != card) card.BorderBrush = lineGray; };

            {
                var menu = new ContextMenu();
                var deleteItem = new MenuItem { Header = "삭제" };
                deleteItem.Click += (_, _) =>
                {
                    SettingsManager.Current.Layouts.Remove(layoutRef);
                    SettingsManager.Save();
                    BuildLayoutCards();
                };
                menu.Items.Add(deleteItem);
                card.ContextMenu = menu;
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

        // 기본 Button 크롬(호버 하늘색 하이라이트, 테두리, 눌림 효과)이 전혀 없는 아이콘 버튼 템플릿.
        // Background은 Transparent로 둔다 — null이면 아이콘 글리프 바깥 여백이 히트테스트를
        // 통과해버려서 클릭 판정이 글자 획에만 걸린다.
        private static ControlTemplate BuildFlatIconButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";                       // 아래 트리거가 TargetName으로 지목한다
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            // 눌렸을 때 1px 내려앉히기 위한 자리. Freezable이라 나중에 통째로 갈아끼운다.
            border.SetValue(UIElement.RenderTransformProperty, new TranslateTransform(0, 0));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            template.VisualTree = border;

            // 상태 표현을 전부 템플릿 한 곳에 모은다(예전엔 호버만 MouseEnter/Leave 핸들러로
            // 처리했는데, 눌림까지 생기면서 두 군데로 갈라지면 상태가 어긋나기 쉬워진다).
            // ⚠ 트리거는 나중에 등록된 것이 이긴다 — 눌림이 호버를 덮어야 하므로 순서가 중요.
            //   Border에 TextBlock.Foreground(= TextElement.Foreground와 같은 DP)를 걸면 Button보다 가까운 조상이라
            //   ContentPresenter가 만든 글리프에 그 값이 적용된다.
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(AccentColor), "Bd"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(AccentDeep), "Bd"));
            pressed.Setters.Add(new Setter(UIElement.RenderTransformProperty, new TranslateTransform(0, 1), "Bd"));
            template.Triggers.Add(pressed);

            return template;
        }

        private void OpenEditorForLayout(ZoneLayout layout)
        {
            if (_selectedMonitor == null) return;
            // 연필 버튼을 연속으로 눌러도 편집창이 겹쳐 뜨지 않게 — 이미 떠 있으면 그 창을
            // 앞으로 가져오기만 한다(별도 안내 없이).
            if (MonitorOverlayEditor.TryActivateExisting()) return;

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
