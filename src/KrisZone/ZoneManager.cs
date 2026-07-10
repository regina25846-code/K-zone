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

        // 배치 후 shadow inset 측정하여 보정.
        // 한 번의 측정값으로 한 번만 보정하면(예전 방식) 같은 창을 같은 zone에 다시 배치할 때
        // shadow inset이 처음 측정값과 달라지는 경우(재배치 직전 창 상태에 따라 값이 흔들림)
        // 과보정/저보정이 나서 왼쪽위로 밀리거나 여백이 남는 문제가 있었음.
        // "현재 보이는 영역(fr)이 목표(px,py,pw,ph)와 얼마나 다른가"를 직접 측정해서 그 차이만큼만
        // raw rect를 옮기는 방식으로 바꾸고, 이걸 값이 수렴할 때까지 최대 3번 반복함 — 어떤
        // 이유로 틀어지든(타이밍, DPI, 이전 상태 등) 결국 목표에 맞춰짐.
        private static void ApplyShadowCorrection(IntPtr hwnd, int px, int py, int pw, int ph)
        {
            int curLeft = px, curTop = py, curRight = px + pw, curBottom = py + ph;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                NativeMethods.DwmFlush();
                NativeMethods.GetWindowRect(hwnd, out var wr);
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                        out var fr, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0) return;

                // 실제 보이는 영역(fr)이 목표(px,py,px+pw,py+ph)와 얼마나 어긋났는지
                int errLeft = px - fr.Left;
                int errTop = py - fr.Top;
                int errRight = (px + pw) - fr.Right;
                int errBottom = (py + ph) - fr.Bottom;

                if (errLeft == 0 && errTop == 0 && errRight == 0 && errBottom == 0) return;

                // raw rect(wr)를 오차만큼 보정 — fr이 wr 안에서 상대적으로 어디 있는지는 유지한 채
                // 목표와의 차이만큼만 이동/확장
                curLeft = wr.Left + errLeft;
                curTop = wr.Top + errTop;
                curRight = wr.Right + errRight;
                curBottom = wr.Bottom + errBottom;

                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                    curLeft, curTop, curRight - curLeft, curBottom - curTop,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
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

            // 1차: zone 크기로 배치
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, px, py, pw, ph,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            // 2차: 배치 후 실제 shadow 측정하여 보정
            ApplyShadowCorrection(hwnd, px, py, pw, ph);
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

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, px, py, pw, ph,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            ApplyShadowCorrection(hwnd, px, py, pw, ph);
        }
    }
}
