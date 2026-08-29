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

        // 파워토이즈 FancyZones 방식(오푸스가 microsoft/PowerToys의 WindowUtils.cpp/WorkArea.cpp
        // 실제 소스와 대조해 확정, 2026-08-29). 창을 건드리기 "전" 안정 상태에서 wr(GetWindowRect,
        // raw rect)과 fr(DwmGetWindowAttribute EXTENDED_FRAME_BOUNDS, 실제 보이는 영역)을 같은
        // 시점에 1회만 읽어서 그림자 마진을 구한다. top은 보정하지 않음 — 그림자가 위로는 안 뻗기
        // 때문(파워토이즈와 동일 관례). 예전의 "배치 후 재측정 → 최대 3회 반복 보정" 방식은
        // 대상 창이 아직 자리를 못 잡은 상태(웨일처럼 느린 창)에서 재면 오차가 폭주해 창이 극단적
        // 으로 쪼그라들고 반복 재배치 자체가 덜컹거림으로 보이는 문제가 있어 전량 제거.
        private static (int left, int right, int bottom) GetShadowMargins(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var wr)) return (0, 0, 0);
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out var fr, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
                return (0, 0, 0);

            int leftMargin = fr.Left - wr.Left;
            int rightMargin = wr.Right - fr.Right;
            int bottomMargin = wr.Bottom - fr.Bottom;
            return (leftMargin, rightMargin, bottomMargin);
        }

        // 목표(px,py,pw,ph)는 "보이는 영역" 기준. raw rect는 그 바깥으로 마진만큼 확장해서
        // SetWindowPos에 1회만 넘긴다. 최소화/최대화 복원을 여기로 모아서 SnapWindow든
        // SnapWindowMulti(다중 존)든 어느 경로를 타든 자동으로 커버되게 함 — 마진 측정도
        // 복원 "후"에 이뤄지도록 순서를 맞춤(복원 전에 재면 마진 값이 틀어짐).
        private static void PlaceWindow(IntPtr hwnd, int px, int py, int pw, int ph)
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            if (NativeMethods.IsZoomed(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            var (leftMargin, rightMargin, bottomMargin) = GetShadowMargins(hwnd);

            int finalLeft = px - leftMargin;
            int finalTop = py;
            int finalRight = px + pw + rightMargin;
            int finalBottom = py + ph + bottomMargin;

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, finalLeft, finalTop,
                finalRight - finalLeft, finalBottom - finalTop,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        // Snap window to zone (in logical coordinates → SetWindowPos uses physical)
        public static void SnapWindow(IntPtr hwnd, ZoneRect zone, MonitorInfo monitor)
        {
            var r = ZoneToPixelRect(zone, monitor);
            double scale = monitor.ScaleFactor;

            int px = (int)Math.Round(r.X * scale);
            int py = (int)Math.Round(r.Y * scale);
            int pw = (int)Math.Round(r.Width * scale);
            int ph = (int)Math.Round(r.Height * scale);

            PlaceWindow(hwnd, px, py, pw, ph);
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

            PlaceWindow(hwnd, px, py, pw, ph);
        }
    }
}
