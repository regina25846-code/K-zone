using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using KrisZone.Settings;

namespace KrisZone
{
    /// <summary>
    /// Listens for window move events (EVENT_SYSTEM_MOVESIZESTART/END + EVENT_OBJECT_LOCATIONCHANGE)
    /// and drives the zone overlay + snap logic.
    /// </summary>
    internal class DragSnapEngine : IDisposable
    {
        private readonly NativeMethods.WinEventDelegate _delegate;
        private IntPtr _hookMoveStart;
        private IntPtr _hookMoveEnd;
        private IntPtr _hookLocation;

        private IntPtr _draggingHwnd = IntPtr.Zero;
        private bool _overlayActive = false;
        private ZoneOverlay? _overlay;
        private MonitorInfo? _currentMonitor;
        private List<int> _highlighted = new();
        private bool _draggingWindowTransparent = false;
        private long _lastLocationTick = 0;
        private DispatcherTimer? _mouseUpWatcher;
        // 이번 드래그에서 스냅 모드(Shift 눌림 등)가 한 번이라도 유효하게 켜졌는지 —
        // 켜졌을 때만 종료 시점 커서 위치로 스냅을 보정한다(일반 드래그는 스냅 안 함)
        private bool _snapEligible = false;

        public DragSnapEngine()
        {
            _delegate = OnWinEvent;
        }

        public void Install()
        {
            uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
            _hookMoveStart = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZESTART, IntPtr.Zero, _delegate, 0, 0, flags);
            _hookMoveEnd = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MOVESIZEEND, NativeMethods.EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _delegate, 0, 0, flags);
            _hookLocation = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _delegate, 0, 0, flags);
        }

        public void Uninstall()
        {
            if (_hookMoveStart != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_hookMoveStart); _hookMoveStart = IntPtr.Zero; }
            if (_hookMoveEnd != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_hookMoveEnd); _hookMoveEnd = IntPtr.Zero; }
            if (_hookLocation != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_hookLocation); _hookLocation = IntPtr.Zero; }
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero) return;
            if (idObject != 0) return; // OBJID_WINDOW = 0

            switch (eventType)
            {
                case NativeMethods.EVENT_SYSTEM_MOVESIZESTART:
                    OnMoveStart(hwnd);
                    break;
                case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                    OnMoveEnd(hwnd);
                    break;
                case NativeMethods.EVENT_OBJECT_LOCATIONCHANGE:
                    if (hwnd == _draggingHwnd)
                        OnLocationChange();
                    break;
            }
        }

        private void OnMoveStart(IntPtr hwnd)
        {
            _draggingHwnd = hwnd;
            _highlighted = new List<int>();
            _snapEligible = false;
            // 투명화는 Shift 누를 때 OnLocationChange에서 처리
        }

        private void ApplyTransparency(IntPtr hwnd)
        {
            if (_draggingWindowTransparent) return;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_LAYERED);
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 180, NativeMethods.LWA_ALPHA);
            _draggingWindowTransparent = true;
        }

        private void RemoveTransparency(IntPtr hwnd)
        {
            if (!_draggingWindowTransparent) return;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_LAYERED);
            _draggingWindowTransparent = false;
        }

        private void OnLocationChange()
        {
            var s = SettingsManager.Current;
            bool shiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
            bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;

            bool shouldShow = !s.ShiftDrag || shiftDown;

            if (!shouldShow)
            {
                // Shift 없으면 투명화 해제 + 오버레이 숨김
                RemoveTransparency(_draggingHwnd);
                HideOverlay();
                return;
            }

            // Shift 누른 순간 투명화 적용
            if (s.MakeDraggedWindowTransparent)
                ApplyTransparency(_draggingHwnd);

            NativeMethods.GetCursorPos(out var pt);
            var monitor = MonitorManager.GetMonitorFromPoint(pt.X, pt.Y);
            if (monitor == null) { HideOverlay(); return; }

            var layout = ZoneManager.GetLayoutForMonitor(monitor);
            if (layout == null || layout.Zones.Count == 0) { HideOverlay(); return; }

            // 스냅 모드가 유효하게 켜진 지점 — 종료 시 커서 보정 스냅을 허용
            _snapEligible = true;

            double scale = monitor.ScaleFactor;
            var cursorLogical = new Point(pt.X / scale, pt.Y / scale);

            int hitIndex = ZoneManager.HitTest(layout, monitor, cursorLogical, layout.SensitivityRadius);

            List<int> newHighlighted;
            if (hitIndex < 0)
            {
                newHighlighted = new List<int>();
            }
            else if (ctrlDown && _highlighted.Count > 0)
            {
                newHighlighted = new List<int>(_highlighted);
                if (!newHighlighted.Contains(hitIndex))
                    newHighlighted.Add(hitIndex);
            }
            else
            {
                newHighlighted = new List<int> { hitIndex };
            }

            // ⚠️ 스냅 정확도의 핵심: _highlighted(어느 구역인지)와 _currentMonitor는 throttle 없이
            // 매 이벤트마다 갱신한다. 예전엔 함수 맨 앞 16ms throttle이 이 계산 전체를 막아서, 빠르게
            // 드래그해 놓으면 마지막 위치가 구역에 도달하기 전 값으로 남아 스냅이 스킵됐음(2026-07-22
            // 형 리포트). HitTest는 가벼우니 매번 해도 무방하고, 무거운 오버레이 렌더링(DrawZones)만
            // 아래에서 16ms throttle을 적용한다.
            bool needNewOverlay = (_overlay == null || !_overlayActive || _currentMonitor?.Handle != monitor.Handle);
            bool highlightChanged = !newHighlighted.SequenceEqual(_highlighted);
            _highlighted = newHighlighted;
            _currentMonitor = monitor;

            if (needNewOverlay)
            {
                // 최초 표시/모니터 전환은 중요한 상태 변화라 throttle 없이 즉시.
                // ZoneOverlay는 하나만 만들어 재사용(Show()가 위치/크기 다시 잡음) — Hide만 하고
                // 새로 만들던 예전 방식은 유령 창이 쌓여 871MB까지 부풀었던 누수 원인이었음.
                var hl = newHighlighted;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _overlay ??= new ZoneOverlay();
                    _overlay.Show(monitor, layout, hl);
                    _overlayActive = true;
                    StartMouseUpWatcher();
                });
            }
            else if (highlightChanged)
            {
                // 같은 모니터 내 하이라이트 변경만 16ms throttle (렌더링 비용 절감)
                long now = System.Environment.TickCount64;
                if (now - _lastLocationTick >= 16)
                {
                    _lastLocationTick = now;
                    var hl = newHighlighted;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                        _overlay?.UpdateHighlight(hl));
                }
            }
        }

        private void OnMoveEnd(IntPtr hwnd)
        {
            if (hwnd != _draggingHwnd) return;
            FinalizeDrag(hwnd);
        }

        // 드래그 종료 처리. 정상적으로는 OnMoveEnd(핸들 일치)로 들어오지만,
        // 크로미움 계열 브라우저의 탭 분리처럼 시작/종료 이벤트의 창 핸들이
        // 달라지는 경우를 대비해 마우스 버튼 상태 감시(StartMouseUpWatcher)에서도 호출됨.
        private void FinalizeDrag(IntPtr hwnd)
        {
            if (_draggingHwnd == IntPtr.Zero && !_overlayActive) return;

            RemoveTransparency(hwnd);
            HideOverlay();

            var highlighted = _highlighted;
            var monitor = _currentMonitor;

            // 드래그 중 위치 추적(OnLocationChange)에 16ms throttle이 걸려있어서, 마우스를 빨리
            // 움직이다 놓거나 구역 경계 근처에서 놓으면 마지막 순간 _highlighted가 비어 스냅이
            // 통째로 스킵되고 창이 놓인 자리에 그대로 남는 문제가 있었음(2026-07-22 형 리포트).
            // 스냅 모드였는데(_snapEligible) 추적값이 비었으면, 놓는 순간 커서의 실제 위치로
            // 구역을 다시 판정해서 그 구역에 확실히 스냅함(PowerToys FancyZones의 mouse-up 판정 방식).
            if (highlighted.Count == 0 && _snapEligible && hwnd != IntPtr.Zero)
            {
                NativeMethods.GetCursorPos(out var pt);
                var m = MonitorManager.GetMonitorFromPoint(pt.X, pt.Y);
                if (m != null)
                {
                    var lay = ZoneManager.GetLayoutForMonitor(m);
                    if (lay != null && lay.Zones.Count > 0)
                    {
                        var cur = new Point(pt.X / m.ScaleFactor, pt.Y / m.ScaleFactor);
                        int hit = ZoneManager.HitTest(lay, m, cur, lay.SensitivityRadius);
                        if (hit >= 0) { highlighted = new List<int> { hit }; monitor = m; }
                    }
                }
            }

            if (hwnd != IntPtr.Zero && highlighted.Count > 0 && monitor != null)
            {
                var layout = ZoneManager.GetLayoutForMonitor(monitor);
                if (layout != null)
                {
                    var snapList = highlighted;
                    var snapMonitor = monitor;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        ZoneManager.SnapWindowMulti(hwnd, snapList, layout, snapMonitor);
                    });
                }
            }

            _draggingHwnd = IntPtr.Zero;
            _highlighted = new List<int>();
            _currentMonitor = null;
            _snapEligible = false;
        }

        private void HideOverlay()
        {
            if (_overlayActive)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _overlay?.Hide());
                _overlayActive = false;
            }
            StopMouseUpWatcher();
        }

        private void StartMouseUpWatcher()
        {
            if (_mouseUpWatcher != null) return;
            _mouseUpWatcher = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
            _mouseUpWatcher.Tick += (s, e) =>
            {
                bool lButtonDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
                if (!lButtonDown) FinalizeDrag(_draggingHwnd);
            };
            _mouseUpWatcher.Start();
        }

        private void StopMouseUpWatcher()
        {
            _mouseUpWatcher?.Stop();
            _mouseUpWatcher = null;
        }

        public void Dispose()
        {
            Uninstall();
            StopMouseUpWatcher();
            // 재사용하던 단일 오버레이도 앱 종료 시 확실히 닫아준다
            System.Windows.Application.Current?.Dispatcher.Invoke(() => { _overlay?.Close(); _overlay = null; });
        }
    }
}
