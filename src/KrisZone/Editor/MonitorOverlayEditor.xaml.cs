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

        public MonitorOverlayEditor(MonitorInfo monitor, Guid? initialLayoutId = null)
        {
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
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo(); return; }
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                _shiftDown = true;
                RefreshSplitterHints();
                return;
            }
            if (e.Key == Key.S && _data != null)
            {
                int zi = GetZoneAtMousePosition();
                if (zi >= 0)
                {
                    var orientation = _shiftDown ? Orientation.Vertical : Orientation.Horizontal;
                    var zone = _data.Zones[zi];
                    int position = orientation == Orientation.Vertical
                        ? (zone.Left + zone.Right) / 2
                        : (zone.Top + zone.Bottom) / 2;
                    if (_data.CanSplit(zi, position, orientation))
                    {
                        PushUndo();
                        _data.Split(zi, position, orientation);
                        SaveCurrentZones();
                        SetupUI();
                    }
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
            _loadingLayout = true;
            LayoutCombo.Items.Clear();
            foreach (var l in SettingsManager.Current.Layouts)
                LayoutCombo.Items.Add(new ComboBoxItem { Content = l.Name, Tag = l });

            var cfg = SettingsManager.Current.MonitorConfigs.FirstOrDefault(c => c.MonitorId == _monitor.Id);
            var targetId = _initialLayoutId ?? cfg?.LayoutId;
            if (targetId.HasValue)
                foreach (ComboBoxItem item in LayoutCombo.Items)
                    if (item.Tag is ZoneLayout l && l.Id == targetId.Value)
                    { LayoutCombo.SelectedItem = item; break; }

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

                if (_currentLayout.Grid == null)
                    _currentLayout.Grid = GridMeta.Default1x1();

                _data = new GridData(_currentLayout.Grid);
                UpdateMinZoneSizes();
            }
            SetupUI();
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
            layout.Grid = GridMeta.Default1x1();
            SettingsManager.Current.Layouts.Add(layout);
            SettingsManager.Save();
            RefreshLayoutCombo();
            LayoutCombo.SelectedIndex = LayoutCombo.Items.Count - 1;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLayout == null) return;
            SaveCurrentZones();
            ZoneManager.AssignLayout(_monitor, _currentLayout.Id);
            Close();
        }

        private void IntroSave_Click(object sender, RoutedEventArgs e)
        {
            IntroPanel.Visibility = Visibility.Collapsed;
            Apply_Click(sender, e);
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

            // ── 존 패널 그리기 ──
            for (int zi = 0; zi < _data.Zones.Count; zi++)
            {
                int zoneIndexCopy = zi;
                var zone = _data.Zones[zi];

                double x = zone.Left   / (double)GridData.Multiplier * pw;
                double y = zone.Top    / (double)GridData.Multiplier * ph;
                double w = (zone.Right  - zone.Left)   / (double)GridData.Multiplier * pw;
                double h = (zone.Bottom - zone.Top)    / (double)GridData.Multiplier * ph;
                const double gap = 3;

                var border = new Border
                {
                    Width  = Math.Max(1, w - gap * 2),
                    Height = Math.Max(1, h - gap * 2),
                    Background = new SolidColorBrush(Color.FromArgb(90, 0x3B, 0x82, 0xF6)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(200, 0x60, 0xA5, 0xFA)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                };

                var label = new TextBlock
                {
                    Text = (zi + 1).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 28, FontWeight = FontWeights.Bold, Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };

                // 존 내부에 분할선 미리보기용 Rectangle 2개 (수평/수직)
                var splitterH = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0, 0x60, 0xA5, 0xFA)), IsHitTestVisible = false };
                var splitterV = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0, 0x60, 0xA5, 0xFA)), IsHitTestVisible = false };
                _splitters.Add((splitterH, splitterV));

                var grid = new Grid();
                grid.Children.Add(label);
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
            }

            // ── 구분선(Resizer) Thumb 그리기 ──
            for (int ri = 0; ri < _data.Resizers.Count; ri++)
            {
                int resizerIndexCopy = ri;
                var resizer = _data.Resizers[ri];

                var thumb = new Thumb
                {
                    Width  = resizer.Orientation == Orientation.Vertical   ? 10 : 80,
                    Height = resizer.Orientation == Orientation.Horizontal ? 10 : 80,
                    Background = new SolidColorBrush(Color.FromArgb(200, 0x94, 0xA3, 0xB8)),
                    Cursor = resizer.Orientation == Orientation.Vertical ? Cursors.SizeWE : Cursors.SizeNS,
                    Opacity = 0.8,
                    Template = BuildThumbTemplate(resizer.Orientation),
                    Focusable = true,
                };

                thumb.DragStarted   += (s, e) => OnResizerDragStarted(resizerIndexCopy);
                thumb.DragDelta     += (s, e) => OnResizerDragDelta(resizerIndexCopy, e);
                thumb.DragCompleted += (s, e) => OnResizerDragCompleted(resizerIndexCopy);
                thumb.GotFocus      += (s, e) => ((Thumb)s).Opacity = 1.0;
                thumb.LostFocus     += (s, e) => ((Thumb)s).Opacity = 0.8;
                thumb.KeyDown       += (s, e) => OnResizerKeyDown(resizerIndexCopy, e);
                thumb.MouseLeftButtonDown += (s, e) => { ((Thumb)s).Focus(); };

                AdornerLayer.Children.Add(thumb);
                PlaceResizer(ri);
            }
        }

        private ControlTemplate BuildThumbTemplate(Orientation orientation)
        {
            var template = new ControlTemplate(typeof(Thumb));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(200, 0x94, 0xA3, 0xB8)));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
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

                thumb.Width  = 10;
                thumb.Height = Math.Max(40, len * 0.6);
                Canvas.SetLeft(thumb, cx - 5);
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
                thumb.Height = 10;
                Canvas.SetLeft(thumb, lx + (len - thumb.Width) / 2);
                Canvas.SetTop(thumb,  cy - 5);
            }
        }

        // ── 존 마우스 이벤트 ──────────────────────────────────────────────────

        private Point? _mouseDownPos;
        private int _activeZone = -1;

        private void OnZoneMouseEnter(int zi, object sender, MouseEventArgs e)
        {
            _activeZone = zi;
        }

        private void OnZoneMouseLeave(int zi, object sender, MouseEventArgs e)
        {
            if (zi < _splitters.Count)
            {
                HideSplitter(zi);
            }
            if (!_inMergeDrag) _mouseDownPos = null;
        }

        private void OnZoneMouseMove(int zi, object sender, MouseEventArgs e)
        {
            if (_inMergeDrag) { DoMergeDrag(e); return; }

            // 마우스 버튼 누른 채 8px 이상 이동 → MergeDrag 시작
            if (_mouseDownPos.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                var cur = e.GetPosition(Preview);
                double dx = Math.Abs(cur.X - _mouseDownPos.Value.X);
                double dy = Math.Abs(cur.Y - _mouseDownPos.Value.Y);
                if (dx > 8 || dy > 8)
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
                var selected = GetSelectedZoneIndices();
                if (selected.Count > 1)
                    ShowMergePanel(e.GetPosition(Preview));
                else
                    ClearSelection();
                return;
            }

            if (_mouseDownPos == null) return;
            var curPos = e.GetPosition(Preview);
            double dx = Math.Abs(curPos.X - _mouseDownPos.Value.X);
            double dy = Math.Abs(curPos.Y - _mouseDownPos.Value.Y);
            _mouseDownPos = null;

            if (dx < 5 && dy < 5)
            {
                // 클릭 → 분할
                DoSplit(zi, e.GetPosition(Preview));
                e.Handled = true;
            }
        }

        private void DoSplit(int zi, Point clickPosInPreview)
        {
            if (_data == null || _currentLayout?.Grid == null) return;

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            var zone = _data.Zones[zi];
            bool isVertical = IsVerticalSplit(zone);
            var orientation = isVertical ? Orientation.Vertical : Orientation.Horizontal;

            int position = isVertical
                ? (int)(clickPosInPreview.X / pw * GridData.Multiplier)
                : (int)(clickPosInPreview.Y / ph * GridData.Multiplier);

            if (!_data.CanSplit(zi, position, orientation)) return;

            PushUndo();
            _data.Split(zi, position, orientation);
            SaveCurrentZones();
            SetupUI();
        }

        private bool IsVerticalSplit(GridData.Zone zone)
        {
            double w = zone.Right  - zone.Left;
            double h = zone.Bottom - zone.Top;
            return (w > h) ^ _shiftDown;
        }

        // ── 분할선 미리보기 ───────────────────────────────────────────────────

        private void UpdateSplitterLine(int zi, Border border, Point localPos)
        {
            if (zi >= _splitters.Count || _data == null) return;
            var (sh, sv) = _splitters[zi];
            var zone = _data.Zones[zi];
            bool vertical = IsVerticalSplit(zone);

            if (vertical)
            {
                sv.Fill = new SolidColorBrush(Color.FromArgb(180, 0x60, 0xA5, 0xFA));
                sv.Width = 3;
                sv.Height = border.Height;
                sv.HorizontalAlignment = HorizontalAlignment.Left;
                sv.Margin = new Thickness(Math.Clamp(localPos.X, 0, border.Width - 3), 0, 0, 0);
                sv.VerticalAlignment = VerticalAlignment.Stretch;

                sh.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            }
            else
            {
                sh.Fill = new SolidColorBrush(Color.FromArgb(180, 0x60, 0xA5, 0xFA));
                sh.Height = 3;
                sh.Width = border.Width;
                sh.VerticalAlignment = VerticalAlignment.Top;
                sh.Margin = new Thickness(0, Math.Clamp(localPos.Y, 0, border.Height - 3), 0, 0);
                sh.HorizontalAlignment = HorizontalAlignment.Stretch;

                sv.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            }
        }

        private void HideSplitter(int zi)
        {
            if (zi >= _splitters.Count) return;
            var (sh, sv) = _splitters[zi];
            sh.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            sv.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        private void RefreshSplitterHints()
        {
            // Shift 상태 변경 시 현재 hover 존 미리보기 갱신은 다음 MouseMove에서 자동 처리
        }

        // ── 구분선 Thumb 드래그 ───────────────────────────────────────────────

        private double _dragAccumX, _dragAccumY;

        private void OnResizerDragStarted(int ri)
        {
            _dragAccumX = 0;
            _dragAccumY = 0;
            HideMergePanel();
        }

        private void OnResizerDragDelta(int ri, DragDeltaEventArgs e)
        {
            if (_data == null) return;
            _dragAccumX += e.HorizontalChange;
            _dragAccumY += e.VerticalChange;

            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;

            var res = _data.Resizers[ri];
            int delta = res.Orientation == Orientation.Vertical
                ? (int)(_dragAccumX / pw * GridData.Multiplier)
                : (int)(_dragAccumY / ph * GridData.Multiplier);

            if (delta == 0 || !_data.CanDrag(ri, delta)) return;

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

        private void OnResizerDragCompleted(int ri)
        {
            if (_data == null || _currentLayout?.Grid == null) return;

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
                SaveCurrentZones();
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

        private void SetZoneSelected(int zi, bool selected)
        {
            if (zi >= Preview.Children.Count) return;
            var border = (Border)Preview.Children[zi];
            border.Background = selected
                ? new SolidColorBrush(Color.FromArgb(160, 0x93, 0xC5, 0xFD))
                : new SolidColorBrush(Color.FromArgb(90, 0x3B, 0x82, 0xF6));
        }

        private List<int> GetSelectedZoneIndices()
        {
            var result = new List<int>();
            for (int zi = 0; zi < Preview.Children.Count; zi++)
            {
                var b = (Border)Preview.Children[zi];
                if (b.Background is SolidColorBrush br && br.Color.A == 160)
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
            Canvas.SetLeft(MergeButtons, Math.Min(pos.X, ActualWidth  - 160));
            Canvas.SetTop(MergeButtons,  Math.Min(pos.Y, ActualHeight - 60));
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
            SaveCurrentZones();
            SetupUI();
        }

        private void MergeCancelClick(object sender, RoutedEventArgs e) => HideMergePanel();

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
            SaveCurrentZones();
            SetupUI();
        }

        // ── Resizer 키보드 지원 (파워토이즈: Delete = 합치기, 방향키 = 이동) ──

        private void OnResizerKeyDown(int ri, KeyEventArgs e)
        {
            if (_data == null || _currentLayout?.Grid == null) return;

            // Delete: 구분선 삭제 → 인접 존 합치기
            if (e.Key == Key.Delete)
            {
                var res = _data.Resizers[ri];
                var indices = new List<int>(res.PositiveSideIndices);
                indices.AddRange(res.NegativeSideIndices);
                PushUndo();
                _data.DoMerge(indices);
                SaveCurrentZones();
                SetupUI();
                e.Handled = true;
                return;
            }

            // 방향키: 구분선 이동
            double pw = Preview.ActualWidth  > 0 ? Preview.ActualWidth  : Width;
            double ph = Preview.ActualHeight > 0 ? Preview.ActualHeight : Height;
            var resizer = _data.Resizers[ri];

            int step = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)
                ? 1   // Ctrl+방향키: 1% 단위
                : 5;  // 방향키: 5% 단위 (GridData.Multiplier 기준)

            int delta = 0;
            bool isVertical = resizer.Orientation == Orientation.Vertical;

            if (isVertical  && e.Key == Key.Left)  delta = -step * 10;
            if (isVertical  && e.Key == Key.Right) delta =  step * 10;
            if (!isVertical && e.Key == Key.Up)    delta = -step * 10;
            if (!isVertical && e.Key == Key.Down)  delta =  step * 10;

            if (delta != 0 && _data.CanDrag(ri, delta))
            {
                PushUndo();
                _data.Drag(ri, delta);
                SaveCurrentZones();
                SetupUI();
                // 포커스를 같은 인덱스 Resizer로 유지
                if (ri < AdornerLayer.Children.Count)
                    ((Thumb)AdornerLayer.Children[Math.Min(ri, AdornerLayer.Children.Count - 1)]).Focus();
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

        private void SaveCurrentZones()
        {
            if (_currentLayout == null || _data == null) return;
            _currentLayout.Zones = _data.ToZoneRects();
            SettingsManager.Save();
        }
    }
}
