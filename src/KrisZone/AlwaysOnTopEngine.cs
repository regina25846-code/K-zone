using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using KrisZone.Settings;

namespace KrisZone
{
    // Win+Ctrl+X로 전경 창을 "항상 위"로 고정/해제 (파워토이즈 AlwaysOnTop 기능과 동일한 동작).
    // 고정된 창은 파란 테두리 오버레이로 표시되고, 창을 움직이면 테두리도 따라다니고,
    // 창을 닫으면 테두리도 자동으로 사라짐.
    internal class AlwaysOnTopEngine : IDisposable
    {
        public static AlwaysOnTopEngine? Instance { get; private set; }

        private readonly Dictionary<IntPtr, PinBorderOverlay> _pinned = new();
        private readonly NativeMethods.WinEventDelegate _locationDelegate;
        private IntPtr _hookLocation;
        private DispatcherTimer? _cleanupTimer;

        public AlwaysOnTopEngine()
        {
            _locationDelegate = OnLocationChanged;
        }

        public void Install()
        {
            if (!SettingsManager.Current.AlwaysOnTopEnabled) return;

            Instance = this;

            uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
            _hookLocation = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _locationDelegate, 0, 0, flags);

            _cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _cleanupTimer.Tick += (_, _) => CleanupClosedWindows();
            _cleanupTimer.Start();

            // 파워토이즈처럼 테마 강조색이 바뀌면 이미 고정된 창의 테두리도 재시작 없이 따라간다.
            AccentColor.Changed += OnAccentChanged;
            AccentColor.StartTracking();
        }

        // SystemEvents는 UI 스레드 보장이 없으므로 Dispatcher로 옮겨서 처리한다.
        private void OnAccentChanged()
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var color = AccentColor.Current();
                foreach (var overlay in _pinned.Values) overlay.SetBorderColor(color);
            });
        }

        public void Toggle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            if (_pinned.ContainsKey(hwnd))
                Unpin(hwnd);
            else
                Pin(hwnd);
        }

        private void Pin(IntPtr hwnd)
        {
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            // 테두리색은 설정값이 아니라 윈도우 테마 강조색을 그때그때 읽어서 쓴다
            // (파워토이즈 AlwaysOnTop 기본값 frameAccentColor = true와 동일).
            var overlay = new PinBorderOverlay();
            _pinned[hwnd] = overlay;
            RepositionOverlay(hwnd, overlay);
            overlay.Show();

            SoundPlayer.Play("PinOn.wav");
        }

        private void Unpin(IntPtr hwnd)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            if (_pinned.TryGetValue(hwnd, out var overlay))
            {
                overlay.Close();
                _pinned.Remove(hwnd);
            }

            SoundPlayer.Play("PinOff.wav");
        }

        private void RepositionOverlay(IntPtr hwnd, PinBorderOverlay overlay)
        {
            // GetWindowRect는 DWM 그림자 여백까지 포함돼서 테두리가 헐렁하게 뜸 —
            // 실제 보이는 영역만 주는 DWMWA_EXTENDED_FRAME_BOUNDS로 타이트하게 맞춤
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out var r, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
            {
                if (!NativeMethods.GetWindowRect(hwnd, out r)) return;
            }
            var monitor = MonitorManager.GetMonitorFromWindow(hwnd);
            overlay.UpdateRect(r, monitor?.ScaleFactor ?? 1.0);
        }

        private void OnLocationChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != 0) return; // idObject 0 = 창 자체(OBJID_WINDOW)
            if (_pinned.TryGetValue(hwnd, out var overlay))
                Application.Current?.Dispatcher.BeginInvoke(() => RepositionOverlay(hwnd, overlay));
        }

        private void CleanupClosedWindows()
        {
            List<IntPtr>? dead = null;
            foreach (var hwnd in _pinned.Keys)
            {
                if (!NativeMethods.IsWindow(hwnd))
                    (dead ??= new List<IntPtr>()).Add(hwnd);
            }
            if (dead == null) return;
            foreach (var hwnd in dead)
            {
                _pinned[hwnd].Close();
                _pinned.Remove(hwnd);
            }
        }

        public void Dispose()
        {
            AccentColor.Changed -= OnAccentChanged;
            AccentColor.StopTracking();
            if (_hookLocation != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_hookLocation); _hookLocation = IntPtr.Zero; }
            _cleanupTimer?.Stop();
            foreach (var overlay in _pinned.Values) overlay.Close();
            _pinned.Clear();
            Instance = null;
        }
    }
}
