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

            // 16ms throttle (60fps 이상 이벤트 skip)
            long now = System.Environment.TickCount64;
            if (now - _lastLocationTick < 16) return;
            _lastLocationTick = now;

            // Shift 누른 순간 투명화 적용
            if (s.MakeDraggedWindowTransparent)
                ApplyTransparency(_draggingHwnd);

            NativeMethods.GetCursorPos(out var pt);
            var monitor = MonitorManager.GetMonitorFromPoint(pt.X, pt.Y);
            if (monitor == null) { HideOverlay(); return; }

            var layout = ZoneManager.GetLayoutForMonitor(monitor);
            if (layout == null || layout.Zones.Count == 0) { HideOverlay(); return; }

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

            if (_overlay == null || !_overlayActive || _currentMonitor?.Handle != monitor.Handle)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _overlay?.Hide();
                    _overlay = new ZoneOverlay();
                    _overlay.Show(monitor, layout, newHighlighted);
                    _overlayActive = true;
                    _currentMonitor = monitor;
                    StartMouseUpWatcher();
                });
            }
            else if (!newHighlighted.SequenceEqual(_highlighted))
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    _overlay?.UpdateHighlight(newHighlighted));
            }

            _highlighted = newHighlighted;
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

            if (hwnd != IntPtr.Zero && _highlighted.Count > 0 && _currentMonitor != null)
            {
                var monitor = _currentMonitor;
                var layout = ZoneManager.GetLayoutForMonitor(monitor);
                if (layout != null)
                {
                    var highlighted = _highlighted;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        ZoneManager.SnapWindowMulti(hwnd, highlighted, layout, monitor);
                    });
                }
            }

            _draggingHwnd = IntPtr.Zero;
            _highlighted = new List<int>();
            _currentMonitor = null;
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
        }
    }
}
