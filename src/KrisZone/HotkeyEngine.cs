using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KrisZone.Settings;

namespace KrisZone
{
    /// <summary>
    /// Registers Win+Ctrl+Arrow hotkeys to move windows between adjacent zones
    /// when OverrideSnapHotkeys is enabled.
    /// </summary>
    internal class HotkeyEngine : IDisposable
    {
        private readonly NativeMethods.WinEventDelegate _winDelegate;
        private IntPtr _focusHook;
        private IntPtr _lastForeground = IntPtr.Zero;

        // Win32 RegisterHotKey
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_WIN = 0x0008;
        private const uint MOD_CTRL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;

        private HotkeyForm? _form;

        public HotkeyEngine()
        {
            _winDelegate = OnWinEvent;
        }

        public void Install()
        {
            if (!SettingsManager.Current.OverrideSnapHotkeys) return;

            _form = new HotkeyForm(this);
            // Accessing Handle auto-creates the native window handle
            var _ = _form.Handle;

            RegisterHotKey(_form.Handle, 1, MOD_WIN | MOD_CTRL | MOD_NOREPEAT, (uint)Keys.Left);
            RegisterHotKey(_form.Handle, 2, MOD_WIN | MOD_CTRL | MOD_NOREPEAT, (uint)Keys.Right);
            RegisterHotKey(_form.Handle, 3, MOD_WIN | MOD_CTRL | MOD_NOREPEAT, (uint)Keys.Up);
            RegisterHotKey(_form.Handle, 4, MOD_WIN | MOD_CTRL | MOD_NOREPEAT, (uint)Keys.Down);
        }

        public void Reinstall()
        {
            Dispose();
            Install();
        }

        internal void OnHotkey(int id)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            var monitor = MonitorManager.GetMonitorFromWindow(hwnd);
            if (monitor == null) return;

            var layout = ZoneManager.GetLayoutForMonitor(monitor);
            if (layout == null || layout.Zones.Count == 0) return;

            // Find current zone of window
            NativeMethods.GetWindowRect(hwnd, out var wr);
            double scale = monitor.ScaleFactor;
            var winCenter = new System.Windows.Point(
                (wr.Left + (wr.Right - wr.Left) / 2.0) / scale,
                (wr.Top + (wr.Bottom - wr.Top) / 2.0) / scale);

            int curZone = ZoneManager.HitTest(layout, monitor, winCenter, 100);
            if (curZone < 0) curZone = 0;

            int cols = EstimateCols(layout);
            int rows = (int)Math.Ceiling((double)layout.Zones.Count / cols);
            int curRow = curZone / cols;
            int curCol = curZone % cols;

            int nextZone = id switch
            {
                1 => curCol > 0 ? curZone - 1 : curZone,        // Left
                2 => curCol < cols - 1 ? curZone + 1 : curZone, // Right
                3 => curZone - cols >= 0 ? curZone - cols : curZone, // Up
                4 => curZone + cols < layout.Zones.Count ? curZone + cols : curZone, // Down
                _ => curZone
            };

            if (nextZone != curZone)
                ZoneManager.SnapWindow(hwnd, layout.Zones[nextZone], monitor);
        }

        private static int EstimateCols(Models.ZoneLayout layout)
        {
            if (layout.Zones.Count == 0) return 1;
            double firstY = layout.Zones[0].Y;
            int cols = 0;
            foreach (var z in layout.Zones)
            {
                if (Math.Abs(z.Y - firstY) < 0.01) cols++;
                else break;
            }
            return Math.Max(1, cols);
        }

        public void Dispose()
        {
            if (_form != null && !_form.IsDisposed)
            {
                UnregisterHotKey(_form.Handle, 1);
                UnregisterHotKey(_form.Handle, 2);
                UnregisterHotKey(_form.Handle, 3);
                UnregisterHotKey(_form.Handle, 4);
                _form.Dispose();
                _form = null;
            }
        }

        private void OnWinEvent(IntPtr h, uint e, IntPtr hwnd, int obj, int child, uint t, uint ts) { }

        private class HotkeyForm : Form
        {
            private readonly HotkeyEngine _engine;
            public HotkeyForm(HotkeyEngine engine) { _engine = engine; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312) // WM_HOTKEY
                    _engine.OnHotkey(m.WParam.ToInt32());
                base.WndProc(ref m);
            }
        }
    }
}
