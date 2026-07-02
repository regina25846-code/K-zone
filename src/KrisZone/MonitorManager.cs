using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KrisZone.Models;

namespace KrisZone
{
    internal class MonitorInfo
    {
        public IntPtr Handle { get; set; }
        public string Id { get; set; } = "";
        public System.Windows.Rect Bounds { get; set; }   // logical px
        public System.Windows.Rect WorkArea { get; set; }
        public double ScaleFactor { get; set; } = 1.0;
    }

    internal static class MonitorManager
    {
        public static List<MonitorInfo> Monitors { get; private set; } = new();

        public static void Refresh()
        {
            Monitors.Clear();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        }

        private static bool Callback(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
        {
            var mi = new NativeMethods.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                // Get DPI scale
                GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _);
                double scale = dpiX > 0 ? dpiX / 96.0 : 1.0;

                var r = mi.rcMonitor;
                var w = mi.rcWork;
                Monitors.Add(new MonitorInfo
                {
                    Handle = hMonitor,
                    Id = mi.szDevice,
                    Bounds = new System.Windows.Rect(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale),
                    WorkArea = new System.Windows.Rect(w.Left / scale, w.Top / scale, (w.Right - w.Left) / scale, (w.Bottom - w.Top) / scale),
                    ScaleFactor = scale
                });
            }
            return true;
        }

        public static MonitorInfo? GetMonitorFromPoint(int x, int y)
        {
            var pt = new NativeMethods.POINT { X = x, Y = y };
            var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            return Monitors.Find(m => m.Handle == hMon);
        }

        public static MonitorInfo? GetMonitorFromWindow(IntPtr hwnd)
        {
            var hMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            return Monitors.Find(m => m.Handle == hMon);
        }

        [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
