using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KrisZone
{
    // ContextMenuStrip은 WinForms가 직접 그리는 옛날 사각형 스타일이라 윈도우11의 둥근 모서리
    // 팝업 스타일이 자동으로 안 먹는다. Electron 앱들(K-Tube 등)의 트레이 메뉴는 내부적으로 이
    // 진짜 Win32 팝업 API(TrackPopupMenuEx)를 그대로 쓰기 때문에 OS 테마를 자동으로 물려받는다.
    // K-Zone 트레이 메뉴를 다른 K-앱들과 똑같은 디자인으로 보이게 하려고 같은 API로 직접 교체함
    // (2026-08-06, 형이 "다른 앱이랑 다르게 투박하다"고 지적).
    internal sealed class NativePopupMenu : NativeWindow, IDisposable
    {
        private const int WM_COMMAND = 0x0111;
        private const uint WM_NULL = 0x0000;
        private const uint MF_STRING = 0x0000;
        private const uint MF_SEPARATOR = 0x0800;
        private const uint MF_CHECKED = 0x0008;
        private const uint TPM_LEFTALIGN = 0x0000;
        private const uint TPM_BOTTOMALIGN = 0x0020;
        private const uint TPM_RIGHTBUTTON = 0x0002;

        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);
        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        public readonly record struct Item(string? Label, bool IsSeparator, bool IsChecked, Action? OnClick)
        {
            public static Item Separator() => new(null, true, false, null);
            public static Item Entry(string label, Action onClick, bool isChecked = false) =>
                new(label, false, isChecked, onClick);
        }

        private readonly Dictionary<uint, Action> _actions = new();

        public NativePopupMenu()
        {
            CreateHandle(new CreateParams { Caption = "KrisZoneTrayMenuHost" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_COMMAND)
            {
                uint id = (uint)(m.WParam.ToInt64() & 0xFFFF);
                if (_actions.TryGetValue(id, out var action))
                    action();
            }
            base.WndProc(ref m);
        }

        public void Show(IReadOnlyList<Item> items)
        {
            _actions.Clear();
            IntPtr hMenu = CreatePopupMenu();
            try
            {
                uint nextId = 1000;
                foreach (var item in items)
                {
                    if (item.IsSeparator)
                    {
                        AppendMenu(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
                        continue;
                    }
                    uint id = nextId++;
                    if (item.OnClick != null) _actions[id] = item.OnClick;
                    uint flags = MF_STRING | (item.IsChecked ? MF_CHECKED : 0);
                    AppendMenu(hMenu, flags, (UIntPtr)id, item.Label);
                }

                GetCursorPos(out var pt);
                SetForegroundWindow(Handle);
                TrackPopupMenuEx(hMenu, TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON, pt.X, pt.Y, Handle, IntPtr.Zero);
                // 트레이 팝업 메뉴가 포커스를 잃어도 안 닫히는 Win32 고전 버그 방지용 표준 우회(MS KB135788).
                PostMessage(Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        public void Dispose() => DestroyHandle();
    }
}
