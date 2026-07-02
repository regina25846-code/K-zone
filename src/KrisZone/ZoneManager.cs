using System;
using System.Linq;
using KrisZone.Models;
using KrisZone.Settings;
using System.Windows;

namespace KrisZone
{
    internal static class ZoneManager
    {
        public static ZoneLayout? GetLayoutForMonitor(MonitorInfo monitor)
        {
            var cfg = SettingsManager.Current.MonitorConfigs.FirstOrDefault(c => c.MonitorId == monitor.Id);
            if (cfg == null) return null;
            return SettingsManager.Current.Layouts.FirstOrDefault(l => l.Id == cfg.LayoutId);
        }

        public static void AssignLayout(MonitorInfo monitor, Guid layoutId)
        {
            var cfg = SettingsManager.Current.MonitorConfigs.FirstOrDefault(c => c.MonitorId == monitor.Id);
            if (cfg == null)
            {
                cfg = new MonitorConfig { MonitorId = monitor.Id };
                SettingsManager.Current.MonitorConfigs.Add(cfg);
            }
            cfg.LayoutId = layoutId;
            SettingsManager.Save();
        }

        // Convert zone (percentage-based) to absolute pixel rect on monitor
        public static Rect ZoneToPixelRect(ZoneRect zone, MonitorInfo monitor)
        {
            var wa = monitor.WorkArea;
            return new Rect(
                wa.X + zone.X * wa.Width,
                wa.Y + zone.Y * wa.Height,
                zone.Width * wa.Width,
                zone.Height * wa.Height);
        }

        // Find which zone index the cursor is in (returns -1 if none)
        public static int HitTest(ZoneLayout layout, MonitorInfo monitor, Point cursorLogical, int sensitivity)
        {
            for (int i = 0; i < layout.Zones.Count; i++)
            {
                var px = ZoneToPixelRect(layout.Zones[i], monitor);
                var expanded = new Rect(
                    px.X - sensitivity, px.Y - sensitivity,
                    px.Width + sensitivity * 2, px.Height + sensitivity * 2);
                if (expanded.Contains(cursorLogical))
                    return i;
            }
            return -1;
        }

        // 창의 보이지 않는 shadow/테두리 inset 계산 (파워토이즈 방식)
        private static (int l, int t, int r, int b) GetWindowInsets(IntPtr hwnd)
        {
            NativeMethods.GetWindowRect(hwnd, out var wr);
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out var fr, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            {
                return (fr.Left - wr.Left, fr.Top - wr.Top,
                        wr.Right - fr.Right, wr.Bottom - fr.Bottom);
            }
            return (0, 0, 0, 0);
        }

        // Snap window to zone (in logical coordinates → SetWindowPos uses physical)
        public static void SnapWindow(IntPtr hwnd, ZoneRect zone, MonitorInfo monitor)
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            var r = ZoneToPixelRect(zone, monitor);
            double scale = monitor.ScaleFactor;

            int px = (int)Math.Round(r.X * scale);
            int py = (int)Math.Round(r.Y * scale);
            int pw = (int)Math.Round(r.Width * scale);
            int ph = (int)Math.Round(r.Height * scale);

            // 파워토이즈 방식: 창 shadow 영역만큼 보정
            var (il, it, ir, ib) = GetWindowInsets(hwnd);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                px - il, py - it, pw + il + ir, ph + it + ib,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        // Snap to multiple zones (bounding box)
        public static void SnapWindowMulti(IntPtr hwnd, System.Collections.Generic.List<int> zoneIndices, ZoneLayout layout, MonitorInfo monitor)
        {
            if (zoneIndices.Count == 0) return;
            if (zoneIndices.Count == 1) { SnapWindow(hwnd, layout.Zones[zoneIndices[0]], monitor); return; }

            var rects = zoneIndices.Select(i => ZoneToPixelRect(layout.Zones[i], monitor)).ToList();
            double minX = rects.Min(r => r.Left);
            double minY = rects.Min(r => r.Top);
            double maxX = rects.Max(r => r.Right);
            double maxY = rects.Max(r => r.Bottom);

            double scale = monitor.ScaleFactor;
            int px = (int)Math.Round(minX * scale);
            int py = (int)Math.Round(minY * scale);
            int pw = (int)Math.Round((maxX - minX) * scale);
            int ph = (int)Math.Round((maxY - minY) * scale);

            var (il, it, ir, ib) = GetWindowInsets(hwnd);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                px - il, py - it, pw + il + ir, ph + it + ib,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }
}
