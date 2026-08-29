using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KrisZone;
using KrisZone.Models;
using KrisZone.Settings;

namespace KrisZone.Editor
{
    public partial class MonitorOverlayEditor : Window
    {
        private readonly MonitorInfo _monitor;
        private readonly Guid? _initialLayoutId;
        private ZoneLayout? _currentLayout;
        private GridData? _data;

        private bool _loadingLayout;
        private bool _shiftDown;

        // merge drag state
        private bool _inMergeDrag;
        private Point _mergeDragStart;
        private int _mergeDragSourceZone = -1;

        // per-zone splitter lines (index matches Preview.Children)
        private readonly List<(Rectangle h, Rectangle v)> _splitters = new();

        private readonly Stack<GridMeta> _undoStack = new();

        // item3: 병합시작(존 밖으로 드래그)과 분할확정(클릭 vs 드래그 판정)이 같은 임계값을 쓰게
        // 통일 — 파워토이즈의 SplitterThickness/2 개념.
        private const double DragThreshold = 8;

        // 리사이저 손잡이 두께(짧은 쪽 변).
        // item7에서 잡기 쉽게 10 → 14로 키웠는데 실기에서 "이미 분할된 선이 너무 두껍다"는
        // 피드백을 받아 절반인 7로 되돌린다(2026-08-29). 포커스 시 강조되는 비율은 유지.
        private const double ResizerThickness = 7;
        private const double ResizerThicknessFocused = 9;

        // 리사이저 색. 예전엔 알파 200에 Opacity 0.8까지 곱해져 실효 알파가 160(63%)이라
        // 어두운 배경에서 선이 묻혔다 — 전부 불투명으로 올린다(Opacity도 1.0 고정).
        // 매 호버마다 브러시를 새로 만들지 않도록 Freeze해서 재사용한다.
        private static readonly Brush ResizerBrush        = MakeFrozen(255, 0x8A, 0x8E, 0x97);
        private static readonly Brush ResizerHoverBrush   = MakeFrozen(255, 0xB4, 0xB8, 0xC0);
        private static readonly Brush ResizerFocusedBrush = MakeFrozen(255, 0x2E, 0x42, 0x72);

        // ── 편집기 단일 인스턴스 ──────────────────────────────────────────────
        // 편집기는 모니터 전체를 덮는 Topmost 오버레이라 두 개가 겹치면 조작 자체가 꼬인다.
        // 게다가 각 인스턴스가 "창을 연 시점" 스냅샷을 따로 들고 있어서(_snapshotGrid),
        // 두 개가 동시에 떠 있으면 나중에 닫힌 쪽의 되돌리기가 먼저 저장한 쪽의 결과를
        // 덮어써버린다. 그래서 레이아웃별이 아니라 앱 전체에서 하나만 뜨게 한다.
        public static MonitorOverlayEditor? Current { get; private set; }

        /// <summary>이미 편집기가 떠 있으면 그 창을 앞으로 가져오고 true를 돌려준다.</summary>
        public static bool TryActivateExisting()
        {
            var existing = Current;
            if (existing == null) return false;

            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            existing.Focus();
            return true;
        }

        public MonitorOverlayEditor(MonitorInfo monitor, Guid? initialLayoutId = null)
        {
            Current = this;
            _monitor = monitor;
            _initialLayoutId = initialLayoutId;
            InitializeComponent();

            var wa = monitor.WorkArea;
            Left   = wa.X;
            Top    = wa.Y;
            Width  = wa.Width;
            Height = wa.Height;

            MonitorLabel.Text = monitor.DisplayName + "  |";

            Loaded  += OnLoaded;
            KeyDown += OnKeyDown;
            KeyUp   += OnKeyUp;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshLayoutCombo();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); return; }
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // 드래그 중에 눌러도 안전하다. Undo가 SetupUI()로 Resizers/Children을 재구성해서
                // 드래그하던 Thumb이 사라져도, 이후 DragDelta/DragCompleted는 인덱스를 실시간
                // 조회해 -1이면 그냥 빠져나간다(ResizerIndexOf 주석 참고). 예전엔 여기에
                // "드래그 중엔 무시" 가드가 있었지만 그 구조적 원인이 없어져 제거했다.
                Undo();
                return;
            }
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _shiftDown = true;
                RefreshSplitterHints();
                return;
            }
            if (e.Key == Key.S && _data != null)
            {
                // 위 Ctrl+Z와 같은 이유로 드래그 중에도 안전하다(가드 제거됨).
                int zi = GetZoneAtMousePosition();
                if (zi >= 0)
                {
                    // S-1: 예전엔 S키 전용으로 방향(Shift면 무조건 세로)/위치(존 정중앙)를 따로
                    // 계산해서, 마우스 미리보기가 쓰는 규칙((가로>세로) XOR Shift + 커서 위치)과
                    // 어긋나는 존이 있었음(가로로 넓은 존에서 Shift 없이 S). 마우스 클릭과 완전히
                    // 같은 DoSplit(zoneIndex, mousePosition) 경로를 그대로 써서 "보이는 미리보기선
                    // = 실제로 생기는 선"이 항상 맞도록 통일.
                    DoSplit(zi, Mouse.GetPosition(Preview));
                    e.Handled = true;
                }
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _shiftDown = false;
                RefreshSplitterHints();
            }
        }

        // ── Layout list ───────────────────────────────────────────────────────

        private void RefreshLayoutCombo()
        {
            var cfg = SettingsManager.Current.MonitorConfigs.FirstOrDefault(c => c.MonitorId == _monitor.Id);
            var targetId = _initialLayoutId ?? cfg?.LayoutId;
            var target = targetId.HasValue
                ? SettingsManager.Current.Layouts.FirstOrDefault(l => l.Id == targetId.Value)
                : null;
            target ??= SettingsManager.Current.Layouts.FirstOrDefault();
            if (target != null) LoadLayout(target);
            else SetupUI();
        }

        // ── 편집 세션 스냅샷 ──────────────────────────────────────────────────
        // 편집기는 _currentLayout.Grid를 "직접" 고친다(GridData가 그 객체를 그대로 들고 있다).
        // 즉 분할/합치기/드래그 한 번마다 SettingsManager.Current 안의 실제 레이아웃이 바뀐다.
        // 그래서 취소/ESC로 닫아도 되돌릴 것이 없었다 — 창을 연 시점의 상태를 따로 떠 놓고,
        // 저장으로 확정하지 않은 채 닫히면 그 상태로 복원한다.
        private GridMeta? _snapshotGrid;
        private List<ZoneRect>? _snapshotZones;
        private string? _snapshotName;
        private bool _committed;   // 저장 버튼으로 확정했는가

        private static GridMeta CloneGrid(GridMeta g) => new GridMeta
        {
            Rows           = g.Rows,
            Columns        = g.Columns,
            RowPercents    = new List<int>(g.RowPercents),
            ColumnPercents = new List<int>(g.ColumnPercents),
            CellChildMap   = new List<int>(g.CellChildMap),
        };

        private void LoadLayout(ZoneLayout layout)
        {
            _currentLayout = layout;
            LayoutNameBox.Text = layout.Name;
            _undoStack.Clear();

            if (_currentLayout.Grid == null)
                _currentLayout.Grid = GridMeta.Default1x1();

            // 되돌릴 기준점. 반드시 GridData를 만들기 "전"에 떠야 한다.
            _snapshotGrid  = CloneGrid(_currentLayout.Grid);
            _snapshotZones = new List<ZoneRect>(_currentLayout.Zones);
            _snapshotName  = _currentLayout.Name;
            _committed     = false;

            _data = new GridData(_currentLayout.Grid);
            UpdateMinZoneSizes();
            SetupUI();
        }

        private void LayoutNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentLayout == null || _loadingLayout) return;
            if (!_currentLayout.IsTemplate)
            {
                // 이름도 편집 중에는 메모리에만 반영한다(취소하면 _snapshotName으로 되돌아간다).
                _currentLayout.Name = LayoutNameBox.Text;
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLayout == null) return;
            ApplyZonesToModel();
            _committed = true;                 // 이 뒤로는 닫혀도 되돌리지 않는다
            // AssignLayout이 SettingsManager.Save()까지 수행한다 — 디스크 반영은 여기 한 번뿐.
            ZoneManager.AssignLayout(_monitor, _currentLayout.Id);
            Close();
        }

        private void IntroSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLayout == null) return;
            IntroPanel.Visibility = Visibility.Collapsed;

            if (_currentLayout.IsTemplate)
            {
                // 템플릿을 편집한 경우엔 사본을 새로 만들어 저장한다.
                ApplyZonesToModel();

                var copy = new ZoneLayout
                {
                    Name     = LayoutNameBox.Text.Trim(),
                    IsTemplate = false,
                    // ⚠ 예전엔 Grid를 그대로 대입해서 사본과 템플릿이 "같은 GridMeta 객체"를
                    //   공유했다. 그러면 이후 어느 한쪽을 고치면 다른 쪽도 같이 바뀌고,
                    //   아래 템플릿 원복까지 사본에 옮겨붙는다. 반드시 깊은 복사.
                    Grid     = _currentLayout.Grid != null ? CloneGrid(_currentLayout.Grid) : GridMeta.Default1x1(),
                    Zones    = new List<ZoneRect>(_currentLayout.Zones),
                };

                // 편집기는 템플릿 원본을 in-place로 고쳐놨으므로 원래 모양으로 되돌린다
                // (사본은 위에서 이미 떠놨으니 영향 없음).
                RevertSession();
                _committed = true;

                SettingsManager.Current.Layouts.Add(copy);
                _currentLayout = copy;
                // AssignLayout이 SettingsManager.Save()까지 수행한다.
                ZoneManager.AssignLayout(_monitor, _currentLayout.Id);
                Close();
            }
            else
            {
                Apply_Click(sender, e);
            }
        }

        private void IntroCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ── Min zone sizes ────────────────────────────────────────────────────

        private void UpdateMinZoneSizes()
        {
            if (_data == null) return;
            double w = ActualWidth  > 0 ? ActualWidth  : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            const int minPx = 80;
            _data.MinZoneWidth  = (int)(GridData.Multiplier / w * minPx);
            _data.MinZoneHeight = (int)(GridData.Multiplier / h * minPx);
        }

        // ── SetupUI (파워토이즈 방식) ─────────────────────────────────────────

        private void SetupUI()
        {
            // D-1: 재구성 직전에 포커스를 가진 리사이저 인덱스를 기억해둔다. Clear()가 포커스
            // 가진 Thumb을 트리에서 제거하면 포커스가 Window로 넘어가버려서(GotFocus/LostFocus
            // 필드 추적 방식은 Clear() 자체가 LostFocus를 유발해 값이 먼저 지워지므로 못 씀)
            // Del/방향키가 조용히 먹통이 됨 — Clear() 호출 "전" 로컬 변수로 잡아둔다.
            int focusedResizerIndex = -1;
            for (int i = 0; i < AdornerLayer.Children.Count; i++)
            {
                if (AdornerLayer.Children[i] is Thumb t && t.IsKeyboardFocused)
                {
                    focusedResizerIndex = i;
                    break;
                }
            }

            Preview.Children.Clear();
            AdornerLayer.Children.Clear();
            _splitters.Clear();
            HideMergePanel();

            if (_data == null || _currentLayout?.Grid == null) return;

            double pw = ActualWidth  > 0 ? ActualWidth  : Width;
            double ph = ActualHeight > 0 ? ActualHeight : Height;
            Preview.Width  = pw;
            Preview.Height = ph;

            UpdateMinZoneSizes();
            // 자석 스냅용 경계선 목록은 그리드가 바뀔 때만 다시 만들면 된다. SetupUI()는
            // 분할/합치기/드래그완료/Undo/레이아웃전환이 전부 거쳐가는 지점이라 여기 한 곳이면
            // 충분하다(마우스 이동 경로에서는 절대 다시 만들지 않는다).
            RebuildKeyPointCache();

            // ── 존 패널 그리기 ──
            for (int zi = 0; zi < _data.Zones.Count; zi++)
            {
                int zoneIndexCopy = zi;
                var zone = _data.Zones[zi];

                double x = zone.Left   / (double)GridData.Multiplier * pw;
                double y = zone.Top    / (double)GridData.Multiplier * ph;
                double w = (zone.Right  - zone.Left)   / (double)GridData.Multiplier * pw;
                double h = (zone.Bottom - zone.Top)    / (double)GridData.Multiplier * ph;
                const double gap = 0;

                // 실제 픽셀 크기 계산
                int pixelW = (int)(w / pw * _monitor.WorkArea.Width);
                int pixelH = (int)(h / ph * _monitor.WorkArea.Height);

                var border = new Border
                {
                    Width  = Math.Max(1, w - gap * 2),
                    Height = Math.Max(1, h - gap * 2),
                    Background = new SolidColorBrush(Color.FromArgb(45, 0x2E, 0x42, 0x72)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0x4A, 0x5F, 0x8C)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                };

                var labelPanel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                var label = new TextBlock
                {
                    Text = (zi + 1).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 36, FontWeight = FontWeights.Bold, Opacity = 0.8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var pixelLabel = new TextBlock
                {
                    Text = $"{pixelW} × {pixelH}",
                    Foreground = Brushes.White,
                    FontSize = 13, Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                labelPanel.Children.Add(label);
                labelPanel.Children.Add(pixelLabel);

                // 존 내부에 분할선 미리보기용 Rectangle 2개 (수평/수직).
                // 크기·정렬은 존이 만들어질 때 한 번만 정하고, 마우스를 따라 움직이는 건
                // RenderTransform(TranslateTransform)으로만 처리한다 — 매 프레임 Margin을
                // 바꿔 레이아웃을 다시 도는 걸 피하기 위함(MoveSplitter 주석 참고).
                // 길이는 Stretch로 두면 존 크기가 바뀌어도 알아서 따라간다.
                var splitterH = new Rectangle
                {
                    Fill = HiddenBrush,
                    IsHitTestVisible = false,
                    Height = SplitterThickness,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    RenderTransform = new TranslateTransform(),
                };
                var splitterV = new Rectangle
                {
                    Fill = HiddenBrush,
                    IsHitTestVisible = false,
                    Width = SplitterThickness,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    RenderTransform = new TranslateTransform(),
                };
                _splitters.Add((splitterH, splitterV));

                var grid = new Grid();
                grid.Children.Add(labelPanel);
                grid.Children.Add(splitterH);
                grid.Children.Add(splitterV);
                border.Child = grid;

                Canvas.SetLeft(border, x + gap);
                Canvas.SetTop(border,  y + gap);
                Preview.Children.Add(border);

                // 마우스 이벤트
                border.MouseEnter += (s, e) => OnZoneMouseEnter(zoneIndexCopy, s, e);
                border.MouseLeave += (s, e) => OnZoneMouseLeave(zoneIndexCopy, s, e);
                border.MouseMove  += (s, e) => OnZoneMouseMove(zoneIndexCopy, s, e);
                border.MouseLeftButtonDown += (s, e) => OnZoneMouseDown(zoneIndexCopy, s, e);
                border.MouseLeftButtonUp   += (s, e) => OnZoneMouseUp(zoneIndexCopy, s, e);

                // item9: 존도 Tab으로 포커스 이동은 가능했는데 시각 표시가 전혀 없었음 —
                // 리사이저 포커스 색(D-2, #2E4272)과 통일된 언어로 테두리를 강조.
                border.GotFocus += (s, e) =>
                {
                    var b = (Border)s;
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x42, 0x72));
                    b.BorderThickness = new Thickness(2);
                };
                border.LostFocus += (s, e) =>
                {
                    var b = (Border)s;
                    b.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0x4A, 0x5F, 0x8C));
                    b.BorderThickness = new Thickness(1);
                };
            }

            // ── 구분선(Resizer) Thumb 그리기 ──
            for (int ri = 0; ri < _data.Resizers.Count; ri++)
            {
                var resizer = _data.Resizers[ri];

                var thumb = new Thumb
                {
                    Width  = resizer.Orientation == Orientation.Vertical   ? ResizerThickness : 80,
                    Height = resizer.Orientation == Orientation.Horizontal ? ResizerThickness : 80,
                    Background = ResizerBrush,
                    Cursor = resizer.Orientation == Orientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS,
                    Opacity = 1.0,
                    Template = BuildThumbTemplate(resizer.Orientation),
                    Focusable = true,
                };

                // ⚠ 인덱스를 람다에 캡처하지 않는다. 드래그 도중 Resizers 목록이 재구성되면
                //   캡처해둔 인덱스가 낡아서 ArgumentOutOfRangeException으로 이어졌다.
                //   파워토이즈처럼 이벤트가 올 때마다 Thumb으로 현재 인덱스를 조회한다.
                thumb.DragStarted   += (s, e) => OnResizerDragStarted();
                thumb.DragDelta     += (s, e) => OnResizerDragDelta((Thumb)s, e);
                thumb.DragCompleted += (s, e) => OnResizerDragCompleted((Thumb)s);
                // D-2: 포커스 표시가 Opacity 변화만으론 거의 안 보인다는 형 피드백 — 배경색과
                // 두께(짧은 쪽 변)도 같이 바꿔서 확실히 구분되게 함(item7: 14→16으로 상수 갱신).
                thumb.GotFocus      += (s, e) =>
                {
                    var t = (Thumb)s;
                    t.Background = ResizerFocusedBrush;
                    if (resizer.Orientation == Orientation.Vertical) t.Width = ResizerThicknessFocused; else t.Height = ResizerThicknessFocused;
                };
                thumb.LostFocus     += (s, e) =>
                {
                    var t = (Thumb)s;
                    t.Background = ResizerBrush;
                    if (resizer.Orientation == Orientation.Vertical) t.Width = ResizerThickness; else t.Height = ResizerThickness;
                };
                // item7: 호버 시에도 살짝 밝아지는 피드백 — 포커스 중엔(더 중요한 상태) 덮어쓰지
                // 않도록 IsKeyboardFocused를 확인.
                thumb.MouseEnter += (s, e) =>
                {
                    var t = (Thumb)s;
                    if (!t.IsKeyboardFocused) t.Background = ResizerHoverBrush;
                };
                thumb.MouseLeave += (s, e) =>
                {
                    var t = (Thumb)s;
                    if (!t.IsKeyboardFocused) t.Background = ResizerBrush;
                };
                thumb.KeyDown       += (s, e) => OnResizerKeyDown((Thumb)s, e);
                thumb.MouseLeftButtonDown += (s, e) => { ((Thumb)s).Focus(); };

                AdornerLayer.Children.Add(thumb);
                PlaceResizer(ri);
            }

            // D-1: 재구성 전 포커스 가진 리사이저가 있었으면 같은 인덱스의 새 Thumb으로 복원.
            // SetupUI()를 부르는 모든 경로(Delete/방향키/드래그완료/Undo/분할/합치기)가 자동으로
            // 이 혜택을 받음 — 개별 호출부마다 따로 Focus()를 다시 걸어줄 필요가 없어짐.
            if (focusedResizerIndex >= 0 && focusedResizerIndex < AdornerLayer.Children.Count)
                ((Thumb)AdornerLayer.Children[focusedResizerIndex]).Focus();
        }

        private ControlTemplate BuildThumbTemplate(Orientation orientation)
        {
            var template = new ControlTemplate(typeof(Thumb));
            var factory = new FrameworkElementFactory(typeof(Border));
            // ⚠ 예전엔 여기에 브러시를 리터럴로 박아놨었다. 그래서 thumb.Background를 아무리
            //   바꿔도(포커스 강조색, 호버 밝아짐) 화면엔 전혀 반영되지 않았다 — 템플릿이
            //   Thumb의 Background를 아예 안 쳐다봤기 때문. TemplateBinding으로 연결해서
            //   Thumb.Background가 실제로 그려지게 한다.
            factory.SetValue(Border.BackgroundProperty,
                             new TemplateBindingExtension(Control.BackgroundProperty));
            // ⚠ CornerRadius는 WPF가 자동으로 클램프해주지 않는다. 짧은 쪽 변의 절반보다 큰 값을
            //   주면 좌우 호가 겹쳐 알약이 아니라 렌즈 모양으로 일그러진다(K-Clock 1.2.2-5에서
            //   실제로 겪은 문제). 기본 두께 7 / 포커스 9 둘 다 안전한 3.5로 둔다.
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(ResizerThickness / 2));
            template.VisualTree = factory;
            return template;
        }

        private void PlaceResizer(int ri)
        {
            if (ri >= AdornerLayer.Children.Count || ri >= _data!.Resizers.Count) return;
            var resizer = _data.Resizers[ri];
            var thumb = (Thumb)AdornerLayer.Children[ri];

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            // 구분선 위치는 양쪽 존들의 경계 중앙
            if (resizer.Orientation == Orientation.Vertical)
            {
                var leftZone  = _data.Zones[resizer.NegativeSideIndices[0]];
                var rightZone = _data.Zones[resizer.PositiveSideIndices[0]];
                var topZone   = _data.Zones[resizer.PositiveSideIndices[0]];
                var botZone   = _data.Zones[resizer.PositiveSideIndices.Last()];

                double cx = leftZone.Right / (double)GridData.Multiplier * pw;
                double ty = topZone.Top    / (double)GridData.Multiplier * ph;
                double by = botZone.Bottom / (double)GridData.Multiplier * ph;
                double len = by - ty;

                thumb.Width  = thumb.IsKeyboardFocused ? ResizerThicknessFocused : ResizerThickness;
                thumb.Height = Math.Max(40, len * 0.6);
                Canvas.SetLeft(thumb, cx - thumb.Width / 2);
                Canvas.SetTop(thumb,  ty + (len - thumb.Height) / 2);
            }
            else
            {
                var topZone  = _data.Zones[resizer.NegativeSideIndices[0]];
                var botZone  = _data.Zones[resizer.PositiveSideIndices[0]];
                var leftZone = _data.Zones[resizer.PositiveSideIndices[0]];
                var rightZone= _data.Zones[resizer.PositiveSideIndices.Last()];

                double cy = topZone.Bottom / (double)GridData.Multiplier * ph;
                double lx = leftZone.Left  / (double)GridData.Multiplier * pw;
                double rx = rightZone.Right/ (double)GridData.Multiplier * pw;
                double len = rx - lx;

                thumb.Width  = Math.Max(40, len * 0.6);
                thumb.Height = thumb.IsKeyboardFocused ? ResizerThicknessFocused : ResizerThickness;
                Canvas.SetLeft(thumb, lx + (len - thumb.Width) / 2);
                Canvas.SetTop(thumb,  cy - thumb.Height / 2);
            }
        }

        // ── 존 마우스 이벤트 ──────────────────────────────────────────────────

        private Point? _mouseDownPos;
        private int _activeZone = -1;

        private void OnZoneMouseEnter(int zi, object sender, MouseEventArgs e)
        {
            _activeZone = zi;
            // 존에 들어온 즉시 미리보기선을 그린다. 예전엔 MouseMove가 한 번 더 와야 선이
            // 나타났는데, 존 경계를 천천히 넘어오거나 분할 직후 SetupUI()로 Border가 통째로
            // 새로 만들어진 직후처럼 MouseMove가 곧바로 안 오는 상황에선 선이 없는 것처럼
            // 보였다(파워토이즈는 OnMouseEnter에서 바로 UpdateSplitter()를 부른다).
            if (_inMergeDrag) return;
            var border = (Border)sender;
            UpdateSplitterLine(zi, border, e.GetPosition(border));
        }

        private void OnZoneMouseLeave(int zi, object sender, MouseEventArgs e)
        {
            if (zi < _splitters.Count)
            {
                HideSplitter(zi);
            }
            // item4: 마우스가 존 밖으로 나가도 눌린 상태(분할 의도)는 유지 — 리셋은 MouseUp에서만.
            // item5 보완: hover 추적(_activeZone)은 여기서 지워야 Shift 즉시반영이 이미 안 보이는
            // 미리보기선을 되살리지 않음(RefreshSplitterHints 참고).
            if (_activeZone == zi) _activeZone = -1;
        }

        private void OnZoneMouseMove(int zi, object sender, MouseEventArgs e)
        {
            if (_inMergeDrag) { DoMergeDrag(e); return; }

            // item3: 병합시작 판정 — 분할확정(OnZoneMouseUp)과 동일한 DragThreshold로 통일.
            if (_mouseDownPos.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                var cur = e.GetPosition(Preview);
                double dx = Math.Abs(cur.X - _mouseDownPos.Value.X);
                double dy = Math.Abs(cur.Y - _mouseDownPos.Value.Y);
                if (dx > DragThreshold || dy > DragThreshold)
                {
                    _inMergeDrag = true;
                    DoMergeDrag(e);
                    return;
                }
            }

            var border = (Border)sender;
            var pos = e.GetPosition(border);
            UpdateSplitterLine(zi, border, pos);
        }

        private void OnZoneMouseDown(int zi, object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(Preview);
            _mergeDragSourceZone = zi;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void OnZoneMouseUp(int zi, object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).ReleaseMouseCapture();

            if (_inMergeDrag)
            {
                _inMergeDrag = false;
                // item4로 MouseLeave에서의 리셋을 없앤 뒤로는 여기서 반드시 지워야 한다 —
                // 안 지우면 병합 드래그 뒤 눌린 좌표가 남아서 다음 hover 때 엉뚱한 판정을 탄다.
                _mouseDownPos = null;
                var selected = GetSelectedZoneIndices();
                if (selected.Count > 1)
                    ShowMergePanel(e.GetPosition(Preview));
                else
                    ClearSelection();
                return;
            }

            // P4: SetupUI()가 존 목록을 다시 만든 직후 낡은 인덱스로 이벤트가 들어올 수 있어
            // 접근 전에 범위를 확인한다.
            if (_mouseDownPos == null || _data == null || zi < 0 || zi >= _data.Zones.Count) return;
            var curPos = e.GetPosition(Preview);
            double dx = Math.Abs(curPos.X - _mouseDownPos.Value.X);
            double dy = Math.Abs(curPos.Y - _mouseDownPos.Value.Y);
            _mouseDownPos = null;

            // item3: 분할확정 판정 — 병합시작(OnZoneMouseMove)과 동일한 DragThreshold로 통일하고,
            // 두 축 다 보지 않고 분할축(가로분할=Y, 세로분할=X)만 검사하도록 변경.
            bool isVertical = IsVerticalSplit(_data.Zones[zi]);
            double axisDelta = isVertical ? dx : dy;
            if (axisDelta < DragThreshold)
            {
                // 클릭 → 분할
                DoSplit(zi, e.GetPosition(Preview));
                e.Handled = true;
            }
        }

        private void DoSplit(int zi, Point clickPosInPreview)
        {
            // P4: 호출 경로가 마우스 클릭과 S키 두 갈래라 여기서도 범위를 확인한다.
            if (_data == null || _currentLayout?.Grid == null) return;
            if (zi < 0 || zi >= _data.Zones.Count) return;

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            var zone = _data.Zones[zi];
            bool isVertical = IsVerticalSplit(zone);
            var orientation = isVertical ? Orientation.Vertical : Orientation.Horizontal;

            int position = isVertical
                ? (int)(clickPosInPreview.X / pw * GridData.Multiplier)
                : (int)(clickPosInPreview.Y / ph * GridData.Multiplier);

            // item1: 같은 축의 기존 그리드 경계선에 자석처럼 붙임. 미리보기와 반드시 같은
            // 함수를 거쳐야 "보이는 선 = 실제 생기는 선"이 유지된다.
            position = SnapSplitPosition(zone, isVertical, position);

            if (!_data.CanSplit(zi, position, orientation)) return;

            PushUndo();
            _data.Split(zi, position, orientation);
            ApplyZonesToModel();
            SetupUI();
        }

        // ⚠ 파워토이즈는 IsVerticalSplit을 "존의 실제 화면 픽셀 크기"로 판정한다
        //   (GridZone.xaml.cs: (ActualWidth > ActualHeight) ^ _switchOrientation).
        //   여기선 Multiplier 좌표(0~10000)로 비교하고 있었는데, 이 좌표계는 화면 종횡비를
        //   무시하기 때문에 값이 전혀 달라진다 — 예를 들어 갓 만든 1x1 레이아웃은 가로세로가
        //   똑같이 10000이라 (w > h)가 항상 false가 되어, 2560x1440 같은 가로로 넓은 화면에서도
        //   파워토이즈와 정반대로 "가로 분할"이 선택됐다. 픽셀 기준으로 바로잡는다.
        private bool IsVerticalSplit(GridData.Zone zone)
        {
            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;
            double w = (zone.Right  - zone.Left) / (double)GridData.Multiplier * pw;
            double h = (zone.Bottom - zone.Top)  / (double)GridData.Multiplier * ph;
            return (w > h) ^ _shiftDown;
        }

        // item1: 지정한 축(세로분할=열 경계, 가로분할=행 경계)의 현재 그리드 내부 경계선들을
        // GridData.Multiplier 스케일로 반환 — MagneticSnap의 keyPoints.
        // ⚠ 파워토이즈는 keyPoints를 "지금 나누려는 존의 내부"로 반드시 걸러서 쓴다
        //   (MagneticSnap.PixelToDataWithSnapping: keyPoints.Where(x => low < x && x < high)).
        //   이 필터가 없으면 존 자신의 좌/우(상/하) 경계선까지 자석 대상이 되어, 경계에서
        //   4% 안쪽만 들어가도 미리보기선이 경계로 빨려가 기존 테두리에 겹쳐 묻히고
        //   CanSplit도 false가 되어 회색으로 바뀐다 — "선이 안 보인다"로 보이는 상태.
        // ⚠ 마우스 이동마다 호출되므로 여기서 리스트를 새로 만들면 안 된다. SetupUI()에서 한 번만
        //   계산해 캐시하고(RebuildKeyPointCache), 여기선 그대로 넘긴다. 존 범위로 거르는 일은
        //   MagneticSnap이 인덱스로 처리한다(할당 0).
        private List<int> _colKeyPoints = new();
        private List<int> _rowKeyPoints = new();

        private void RebuildKeyPointCache()
        {
            _colKeyPoints = BuildKeyPoints(_currentLayout?.Grid?.ColumnPercents);
            _rowKeyPoints = BuildKeyPoints(_currentLayout?.Grid?.RowPercents);

            static List<int> BuildKeyPoints(List<int>? percents)
            {
                if (percents == null) return new List<int>();
                var pfx = GridData.PrefixSum(percents);
                var result = new List<int>(pfx.Count);
                foreach (int p in pfx)
                    if (p > 0 && p < GridData.Multiplier) result.Add(p);
                return result;   // PrefixSum은 오름차순이라 정렬 그대로 유지된다
            }
        }

        // 분할 위치(Multiplier 좌표)를 같은 축의 기존 경계선에 자석 스냅한 뒤 존 안으로 클램프.
        // 미리보기(UpdateSplitterLine)와 실제 분할(DoSplit)이 반드시 이 하나를 공유해야
        // "보이는 선 = 생기는 선"이 깨지지 않는다. 파워토이즈도 마지막에 존 범위로 클램프한다
        // (MagneticSnap.PixelToDataWithSnapping 끝의 Math.Clamp(result, low, high)).
        private int SnapSplitPosition(GridData.Zone zone, bool vertical, int rawPosition)
        {
            int low  = vertical ? zone.Left  : zone.Top;
            int high = vertical ? zone.Right : zone.Bottom;
            if (high <= low) return rawPosition;

            return MagneticSnap.Snap(rawPosition,
                                     vertical ? _colKeyPoints : _rowKeyPoints,
                                     low, high);
        }

        // ── 분할선 미리보기 ───────────────────────────────────────────────────

        // 미리보기선 두께(px).
        private const double SplitterThickness = 3;

        // item2: 분할 가능/불가에 따라 색을 가른다.
        // ⚠ 무효색을 알파 100(39%) 회색으로 두면 이 편집기의 밝은 크림색 바탕(창 배경
        //   #85E6E4DF + 존 배경 알파 45)에서 거의 보이지 않는다 — 두 색 모두 확실히 보이는
        //   알파를 쓴다.
        // ⚠ 마우스 이동마다 new SolidColorBrush(...)를 만들면 매 프레임 할당 + GC 부담이
        //   생기고, 같은 색이어도 객체가 달라 렌더가 계속 무효화된다. static readonly로 한 번만
        //   만들고 Freeze()해서 재사용한다(Freeze한 Brush는 WPF가 더 빠른 경로로 그린다).
        private static readonly Brush ValidBrush   = MakeFrozen(180, 0x4A, 0x5F, 0x8C);
        private static readonly Brush InvalidBrush = MakeFrozen(170, 0x6B, 0x70, 0x7A);
        private static readonly Brush HiddenBrush  = MakeFrozen(0, 0, 0, 0);

        private static Brush MakeFrozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        // 존 Border의 실제 렌더 크기. Width/Height가 아직 확정 전이거나(NaN) 0이면 그 값으로
        // 나누는 순간 좌표가 NaN이 되고, NaN이 Thickness에 들어가면 WPF가 그 Rectangle을
        // 아무 예외 없이 그냥 안 그린다(=선이 통째로 사라지고 crash.log에도 안 남는다).
        // ActualWidth → Width → 존 크기로 환산한 값 순으로 반드시 양수를 확보한다.
        private static double SafeExtent(double actual, double declared, double fallback)
        {
            if (actual > 0 && !double.IsNaN(actual)) return actual;
            if (declared > 0 && !double.IsNaN(declared)) return declared;
            return fallback > 0 ? fallback : 1;
        }

        private void UpdateSplitterLine(int zi, Border border, Point localPos)
        {
            if (_data == null || zi < 0 || zi >= _splitters.Count || zi >= _data.Zones.Count) return;
            var (sh, sv) = _splitters[zi];
            var zone = _data.Zones[zi];
            bool vertical = IsVerticalSplit(zone);
            var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;
            double bw = SafeExtent(border.ActualWidth,  border.Width,
                                   (zone.Right  - zone.Left) / (double)GridData.Multiplier * pw);
            double bh = SafeExtent(border.ActualHeight, border.Height,
                                   (zone.Bottom - zone.Top)  / (double)GridData.Multiplier * ph);

            if (vertical)
            {
                // border 로컬 px → 전역 Multiplier 좌표 → (DoSplit과 동일한) 자석 스냅 →
                // 다시 로컬 px. 실제 분할과 같은 SnapSplitPosition을 써야 어긋나지 않는다.
                int span = Math.Max(1, zone.Right - zone.Left);
                int rawX = zone.Left + (int)(localPos.X / bw * span);
                int snappedX = SnapSplitPosition(zone, true, rawX);
                double localX = (snappedX - zone.Left) / (double)span * bw;

                SetSplitterFill(sv, _data.CanSplit(zi, snappedX, orientation) ? ValidBrush : InvalidBrush);
                MoveSplitter(sv, SafeOffset(localX, bw), 0);
                SetSplitterFill(sh, HiddenBrush);
            }
            else
            {
                int span = Math.Max(1, zone.Bottom - zone.Top);
                int rawY = zone.Top + (int)(localPos.Y / bh * span);
                int snappedY = SnapSplitPosition(zone, false, rawY);
                double localY = (snappedY - zone.Top) / (double)span * bh;

                SetSplitterFill(sh, _data.CanSplit(zi, snappedY, orientation) ? ValidBrush : InvalidBrush);
                MoveSplitter(sh, 0, SafeOffset(localY, bh));
                SetSplitterFill(sv, HiddenBrush);
            }
        }

        // 미리보기선을 옮길 때 Margin을 쓰면 마우스가 움직일 때마다 존 Border 안 Grid 전체가
        // measure/arrange를 다시 돈다 — 그 Grid엔 존 번호와 "1280 × 720" 같은 TextBlock이 들어
        // 있어서 텍스트 측정까지 매 프레임 다시 하게 되고, 이게 선이 끊겨 보이는 주된 원인이었다.
        // RenderTransform은 레이아웃을 건드리지 않고 렌더 단계에서만 반영되므로 훨씬 가볍다.
        private static void MoveSplitter(Rectangle r, double x, double y)
        {
            if (r.RenderTransform is TranslateTransform t)
            {
                if (t.X != x) t.X = x;
                if (t.Y != y) t.Y = y;
            }
        }

        // 같은 브러시를 다시 대입하면 값이 같아도 "다른 객체"라 렌더가 매번 무효화된다.
        // 참조가 실제로 바뀔 때만 대입한다.
        private static void SetSplitterFill(Rectangle r, Brush b)
        {
            if (!ReferenceEquals(r.Fill, b)) r.Fill = b;
        }

        // Margin 오프셋을 [0, extent-두께] 안으로 넣되, 존이 두께보다 얇아 상한이 하한보다
        // 작아지는 경우(리사이저를 끝까지 끌면 존 Border가 1px까지 줄 수 있다)에도
        // Math.Clamp가 ArgumentException을 던지지 않도록 직접 처리한다. NaN도 0으로 흡수.
        private static double SafeOffset(double value, double extent)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            double max = extent - SplitterThickness;
            if (max <= 0) return 0;
            return Math.Clamp(value, 0, max);
        }

        private void HideSplitter(int zi)
        {
            if (zi >= _splitters.Count) return;
            var (sh, sv) = _splitters[zi];
            SetSplitterFill(sh, HiddenBrush);
            SetSplitterFill(sv, HiddenBrush);
        }

        private void RefreshSplitterHints()
        {
            // item5: Shift 상태 변경 즉시 현재 hover 중인 존의 미리보기 방향을 다시 계산 —
            // 마우스를 다시 움직이지 않아도 반영되게 함. _activeZone은 OnZoneMouseEnter/Leave가
            // 추적(존 밖으로 나가면 -1로 리셋되므로 존을 벗어난 뒤엔 여기서 아무 일도 안 함).
            if (_activeZone < 0 || _activeZone >= Preview.Children.Count) return;
            var border = (Border)Preview.Children[_activeZone];
            var pos = Mouse.GetPosition(border);
            UpdateSplitterLine(_activeZone, border, pos);
        }

        // ── 구분선 Thumb 드래그 ───────────────────────────────────────────────

        private double _dragAccumX, _dragAccumY;

        // 이벤트가 발생한 그 순간의 리사이저 인덱스를 조회한다. 드래그 도중 Delete/분할/Undo로
        // Resizers 목록이 재구성돼 그 Thumb이 화면에서 사라졌으면 -1이 나오고, 호출부는 조용히
        // 빠져나간다. 파워토이즈 GridEditor.xaml.cs가 쓰는 방식 그대로다:
        //   int resizerIndex = AdornerLayer.Children.IndexOf(resizer);
        //   if (resizerIndex == -1) { /* Resizer was removed during drag */ return; }
        // 예전엔 인덱스를 람다에 캡처해뒀다가 낡은 값으로 _data.Resizers[ri]를 건드려
        // ArgumentOutOfRangeException이 났고(7/8·7/19 크래시로그), 그걸 막으려고 "드래그 중엔
        // 단축키를 전부 무시"하는 가드를 뒀었다. 이 방식으로 바꾸면서 그 가드는 불필요해져 제거.
        private int ResizerIndexOf(Thumb thumb)
        {
            if (_data == null) return -1;
            int ri = AdornerLayer.Children.IndexOf(thumb);
            return (ri >= 0 && ri < _data.Resizers.Count) ? ri : -1;
        }

        private void OnResizerDragStarted()
        {
            _dragAccumX = 0;
            _dragAccumY = 0;
            HideMergePanel();
        }

        private void OnResizerDragDelta(Thumb sender, DragDeltaEventArgs e)
        {
            if (_data == null) return;
            int ri = ResizerIndexOf(sender);
            if (ri < 0) return;   // 드래그 중 이 리사이저가 사라짐

            _dragAccumX += e.HorizontalChange;
            _dragAccumY += e.VerticalChange;

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            var res = _data.Resizers[ri];
            int delta = res.Orientation == Orientation.Vertical
                ? (int)(_dragAccumX / pw * GridData.Multiplier)
                : (int)(_dragAccumY / ph * GridData.Multiplier);

            if (delta == 0) return;
            if (!_data.CanDrag(ri, delta))
            {
                // item6: 한계에 걸리면 이번 프레임 변화량을 누적값에서 되돌려서, 한계를 넘긴
                // 만큼 되감아야 반응이 시작되는 고무줄 지연 현상을 없앰(파워토이즈 방식).
                if (res.Orientation == Orientation.Vertical) _dragAccumX -= e.HorizontalChange;
                else _dragAccumY -= e.VerticalChange;
                return;
            }

            // UI 즉시 업데이트 (존 위치)
            if (res.Orientation == Orientation.Vertical)
            {
                foreach (int zi in res.PositiveSideIndices)
                {
                    var b = (Border)Preview.Children[zi];
                    Canvas.SetLeft(b, Canvas.GetLeft(b) + e.HorizontalChange);
                    b.Width = Math.Max(1, b.Width - e.HorizontalChange);
                }
                foreach (int zi in res.NegativeSideIndices)
                {
                    var b = (Border)Preview.Children[zi];
                    b.Width = Math.Max(1, b.Width + e.HorizontalChange);
                }
            }
            else
            {
                foreach (int zi in res.PositiveSideIndices)
                {
                    var b = (Border)Preview.Children[zi];
                    Canvas.SetTop(b, Canvas.GetTop(b) + e.VerticalChange);
                    b.Height = Math.Max(1, b.Height - e.VerticalChange);
                }
                foreach (int zi in res.NegativeSideIndices)
                {
                    var b = (Border)Preview.Children[zi];
                    b.Height = Math.Max(1, b.Height + e.VerticalChange);
                }
            }

            // 다른 Resizer 위치 갱신
            for (int i = 0; i < AdornerLayer.Children.Count; i++)
                if (i != ri) PlaceResizer(i);

            // 현재 Resizer도 이동
            var thumb = (Thumb)AdornerLayer.Children[ri];
            if (res.Orientation == Orientation.Vertical)
                Canvas.SetLeft(thumb, Canvas.GetLeft(thumb) + e.HorizontalChange);
            else
                Canvas.SetTop(thumb, Canvas.GetTop(thumb) + e.VerticalChange);
        }

        private void OnResizerDragCompleted(Thumb sender)
        {
            if (_data == null || _currentLayout?.Grid == null) return;

            int ri = ResizerIndexOf(sender);
            // 드래그 도중 Delete/분할/Undo로 이 리사이저가 이미 사라진 경우. 그때는 SetupUI()가
            // 이미 다시 그려놨으므로 여기서 할 일이 없다(파워토이즈와 동일).
            if (ri < 0) return;

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            var res = _data.Resizers[ri];
            int delta = res.Orientation == Orientation.Vertical
                ? (int)(_dragAccumX / pw * GridData.Multiplier)
                : (int)(_dragAccumY / ph * GridData.Multiplier);

            if (delta != 0 && _data.CanDrag(ri, delta))
            {
                PushUndo();
                _data.Drag(ri, delta);
                ApplyZonesToModel();
            }

            SetupUI();
        }

        // ── 존 합치기 ─────────────────────────────────────────────────────────

        private void DoMergeDrag(MouseEventArgs e)
        {
            var pos = e.GetPosition(Preview);
            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            if (!_inMergeDrag)
            {
                _inMergeDrag = true;
                _mergeDragStart = pos;
            }

            int lx = (int)(Math.Min(_mergeDragStart.X, pos.X) / pw * GridData.Multiplier);
            int rx = (int)(Math.Max(_mergeDragStart.X, pos.X) / pw * GridData.Multiplier);
            int ty = (int)(Math.Min(_mergeDragStart.Y, pos.Y) / ph * GridData.Multiplier);
            int by = (int)(Math.Max(_mergeDragStart.Y, pos.Y) / ph * GridData.Multiplier);

            if (_data == null) return;
            var selectedIndices = new List<int>();
            for (int zi = 0; zi < _data.Zones.Count; zi++)
            {
                var z = _data.Zones[zi];
                bool sel = Math.Max(z.Left, lx) <= Math.Min(z.Right, rx) &&
                           Math.Max(z.Top,  ty) <= Math.Min(z.Bottom, by);
                SetZoneSelected(zi, sel);
                if (sel) selectedIndices.Add(zi);
            }

            _data.MergeClosureIndices(selectedIndices).ForEach(zi => SetZoneSelected(zi, true));
        }

        // item8: 선택 하이라이트 대비 강화 — 알파 90(옅음) → 180(거의 불투명)으로. 이 값은
        // GetSelectedZoneIndices의 판정 기준과 반드시 같이 맞춰야 함(아래 SelectedAlpha 참고).
        private const byte SelectedAlpha = 180;

        private void SetZoneSelected(int zi, bool selected)
        {
            if (zi >= Preview.Children.Count) return;
            var border = (Border)Preview.Children[zi];
            border.Background = selected
                ? new SolidColorBrush(Color.FromArgb(SelectedAlpha, 0x7C, 0x8F, 0xBD))
                : new SolidColorBrush(Color.FromArgb(45, 0x2E, 0x42, 0x72));
        }

        private List<int> GetSelectedZoneIndices()
        {
            var result = new List<int>();
            for (int zi = 0; zi < Preview.Children.Count; zi++)
            {
                var b = (Border)Preview.Children[zi];
                if (b.Background is SolidColorBrush br && br.Color.A == SelectedAlpha)
                    result.Add(zi);
            }
            return result;
        }

        private void ClearSelection()
        {
            for (int zi = 0; zi < Preview.Children.Count; zi++)
                SetZoneSelected(zi, false);
            _inMergeDrag = false;
        }

        private void ShowMergePanel(Point pos)
        {
            MergePanel.Visibility = Visibility.Visible;
            // 액션바가 화면 밖으로 나가지 않게 클램프. 처음 띄울 땐 아직 레이아웃 전이라
            // ActualWidth/Height가 0일 수 있어서 대략적인 기본값으로 대체한다(그림자 여백 12px).
            double w = MergeButtons.ActualWidth  > 0 ? MergeButtons.ActualWidth  : 220;
            double h = MergeButtons.ActualHeight > 0 ? MergeButtons.ActualHeight : 52;
            Canvas.SetLeft(MergeButtons, Math.Max(0, Math.Min(pos.X, ActualWidth  - w - 12)));
            Canvas.SetTop(MergeButtons,  Math.Max(0, Math.Min(pos.Y, ActualHeight - h - 12)));
        }

        private void HideMergePanel()
        {
            MergePanel.Visibility = Visibility.Collapsed;
            ClearSelection();
        }

        private void MergeClick(object sender, RoutedEventArgs e)
        {
            if (_data == null || _currentLayout?.Grid == null) return;
            var selected = GetSelectedZoneIndices();
            HideMergePanel();
            if (selected.Count < 2) return;
            PushUndo();
            _data.DoMerge(selected);
            ApplyZonesToModel();
            SetupUI();
        }

        private void MergeCancelClick(object sender, RoutedEventArgs e) => HideMergePanel();

        // 합치기 버튼 바깥(백드롭)을 클릭하면 취소 — 파워토이즈 MergePanelMouseUp과 동일.
        // 버튼 자체를 누른 경우엔 Button이 MouseUp을 Handled 처리하므로 여기까지 안 온다.
        private void MergePanelMouseUp(object sender, MouseButtonEventArgs e) => HideMergePanel();

        // ── Undo ─────────────────────────────────────────────────────────────

        private void PushUndo()
        {
            if (_currentLayout?.Grid == null) return;
            var g = _currentLayout.Grid;
            var snapshot = new GridMeta
            {
                Rows    = g.Rows, Columns = g.Columns,
                RowPercents    = new List<int>(g.RowPercents),
                ColumnPercents = new List<int>(g.ColumnPercents),
                CellChildMap   = new List<int>(g.CellChildMap),
            };
            _undoStack.Push(snapshot);
        }

        private void Undo()
        {
            if (_undoStack.Count == 0 || _currentLayout == null) return;
            _currentLayout.Grid = _undoStack.Pop();
            _data = new GridData(_currentLayout.Grid);
            UpdateMinZoneSizes();
            ApplyZonesToModel();
            SetupUI();
        }

        // ── Resizer 키보드 지원 (파워토이즈: Delete = 합치기, 방향키 = 이동) ──

        private void OnResizerKeyDown(Thumb sender, KeyEventArgs e)
        {
            if (_data == null || _currentLayout?.Grid == null) return;

            // 마우스로 구분선을 누르고 있는 중에도 그대로 동작한다(파워토이즈와 동일).
            // 예전엔 "드래그 중엔 무시" 가드가 있어서 마우스를 떼야만 Del이 먹었는데,
            // 인덱스를 실시간 조회하도록 바꾸면서 그 가드가 필요 없어졌다.
            int ri = ResizerIndexOf(sender);
            if (ri < 0) return;

            // Delete: 구분선 삭제 → 인접 존 합치기
            if (e.Key == Key.Delete)
            {
                var res = _data.Resizers[ri];
                var indices = new List<int>(res.PositiveSideIndices);
                indices.AddRange(res.NegativeSideIndices);
                PushUndo();
                _data.DoMerge(indices);
                ApplyZonesToModel();
                SetupUI();
                e.Handled = true;
                return;
            }

            // 방향키: 구분선 이동
            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;
            var resizer = _data.Resizers[ri];

            // item10: 기존엔 *10이 곱해져서 방향키 0.5%(1920px 기준 약 9.6px)/Ctrl+방향키 0.1%로
            // 너무 성큼성큼 움직였음. *10을 빼서 방향키 0.05%(약 1px 상당)/Ctrl+방향키 0.01%로
            // 10배 세밀하게 — step 값(1, 5)과 그 5:1 비율 자체는 그대로 유지.
            int step = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)
                ? 1   // Ctrl+방향키: 0.01% 단위 (초미세)
                : 5;  // 방향키: 0.05% 단위 (GridData.Multiplier 기준, 약 1px 상당)

            int delta = 0;
            bool isVertical = resizer.Orientation == Orientation.Vertical;

            if (isVertical  && e.Key == Key.Left)  delta = -step;
            if (isVertical  && e.Key == Key.Right) delta =  step;
            if (!isVertical && e.Key == Key.Up)    delta = -step;
            if (!isVertical && e.Key == Key.Down)  delta =  step;

            if (delta != 0 && _data.CanDrag(ri, delta))
            {
                PushUndo();
                _data.Drag(ri, delta);
                ApplyZonesToModel();
                // 포커스 복원은 이제 SetupUI() 내부에서 D-1 로직으로 일괄 처리됨(이 자리에
                // 개별적으로 있던 재포커스 코드는 그 로직으로 흡수돼 중복 제거).
                SetupUI();
                e.Handled = true;
            }
        }

        // ── 마우스 위치 기반 존 검색 ─────────────────────────────────────────

        private int GetZoneAtMousePosition()
        {
            if (_data == null) return -1;
            var mousePos = Mouse.GetPosition(Preview);
            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            double mx = mousePos.X / pw * GridData.Multiplier;
            double my = mousePos.Y / ph * GridData.Multiplier;

            for (int zi = 0; zi < _data.Zones.Count; zi++)
            {
                var z = _data.Zones[zi];
                if (mx >= z.Left && mx <= z.Right && my >= z.Top && my <= z.Bottom)
                    return zi;
            }
            return -1;
        }

        // ── 저장 ─────────────────────────────────────────────────────────────

        // ⚠ 이름 그대로 "모델에만" 반영한다. 디스크 저장(SettingsManager.Save)은 하지 않는다 —
        //   그건 저장 버튼 경로(Apply_Click / IntroSave_Click)에서만 일어난다.
        //   예전엔 이 함수가 조작 한 번마다 디스크까지 썼기 때문에 취소/ESC가 의미가 없었다.
        private void ApplyZonesToModel()
        {
            if (_currentLayout == null || _data == null) return;
            _currentLayout.Zones = _data.ToZoneRects();
        }

        // 저장하지 않고 닫힌 경우, 창을 열던 시점 상태로 되돌린다.
        private void RevertSession()
        {
            if (_currentLayout == null) return;
            if (_snapshotGrid  != null) _currentLayout.Grid  = _snapshotGrid;
            if (_snapshotZones != null) _currentLayout.Zones = _snapshotZones;
            if (_snapshotName  != null) _currentLayout.Name  = _snapshotName;
            // 디스크는 애초에 건드리지 않았으므로 저장할 것도 없다.
        }

        // 취소 버튼 / ESC / X 버튼 / 작업표시줄 닫기 — 닫히는 모든 경로가 여기를 지난다.
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // 혹시 모를 경합에 대비해 "내가 현재 인스턴스일 때만" 비운다.
            if (ReferenceEquals(Current, this)) Current = null;
            if (!_committed) RevertSession();
        }
    }
}
