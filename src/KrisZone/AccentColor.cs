using System;
using System.Windows.Media;
using Microsoft.Win32;

namespace KrisZone
{
    // 윈도우 테마 강조색(Accent)을 읽어오고, 사용자가 테마색을 바꾸면 알려준다.
    //
    // ── 파워토이즈 원본 대조 (2026-08-29) ─────────────────────────────────────
    // AlwaysOnTop 모듈은 테두리를 그릴 때마다 강조색을 새로 읽는다:
    //   WindowBorder.cpp:199
    //     if (settings->frameAccentColor) {
    //         winrt::Windows::UI::ViewManagement::UISettings settings;
    //         auto accentValue = settings.GetColorValue(UIColorType::Accent);
    //         color = RGB(accentValue.R, accentValue.G, accentValue.B);
    //     }
    // 그리고 테마색이 바뀌면 즉시 다시 그린다(재시작 불필요):
    //   Settings.cpp:52
    //     m_uiSettings.ColorValuesChanged([&](...) {
    //         if (currentSettings->frameAccentColor) NotifyObservers(SettingId::FrameAccentColor);
    //     });
    // 기본값은 frameAccentColor = true, frameThickness = 4 (AlwaysOnTopProperties.cs).
    //
    // ── 이식 방식과 차이점 ────────────────────────────────────────────────────
    // 파워토이즈가 쓰는 WinRT UISettings를 .NET에서 부르려면 프로젝트 TFM을
    // net8.0-windows → net8.0-windows10.0.x 로 올려 Windows SDK 프로젝션을 붙여야 한다.
    // 지금 이 앱은 맥에서 크로스 빌드(EnableWindowsTargeting)하고 있어서 TFM 변경은
    // 빌드 자체를 흔들 위험이 커, 같은 값을 담고 있는 레지스트리를 읽는 방식으로 이식했다.
    // 갱신 시점(테마 바뀌면 재시작 없이 즉시 반영)과 기본 두께 4px는 원본과 동일하게 맞췄다.
    // ⚠ 레지스트리 값과 UISettings의 Accent가 이론상 1:1이 아닐 수 있다 — 실기에서 윈도우
    //   설정의 강조색과 눈에 띄게 다르면 이 파일만 보면 된다.
    internal static class AccentColor
    {
        // 파워토이즈 AlwaysOnTopProperties.cs의 DefaultFrameColor. 강조색을 못 읽을 때만 쓴다.
        private const string FallbackHex = "#0099cc";

        // 파워토이즈 DefaultFrameThickness.
        public const double FrameThickness = 4;

        /// <summary>테마색이 바뀌었을 때 발생(UI 스레드 보장 없음 — 구독하는 쪽에서 Dispatcher로 옮길 것).</summary>
        public static event Action? Changed;

        private static bool _hooked;

        public static void StartTracking()
        {
            if (_hooked) return;
            _hooked = true;
            // 파워토이즈의 UISettings.ColorValuesChanged에 대응하는 .NET 쪽 알림.
            // 강조색/테마 변경은 General 또는 Color 범주로 올라온다.
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public static void StopTracking()
        {
            if (!_hooked) return;
            _hooked = false;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.Color ||
                e.Category == UserPreferenceCategory.VisualStyle)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>현재 윈도우 강조색. 매번 새로 읽는다(파워토이즈도 그릴 때마다 읽는다).</summary>
        public static Color Current()
        {
            // DWM이 창 테두리에 실제로 쓰는 강조색. 우리가 그리는 것도 창 테두리라 의미가 같다.
            if (TryReadAbgr(@"Software\Microsoft\Windows\DWM", "AccentColor", out var c)) return c;
            // 보조: 메뉴 등에 쓰이는 강조색.
            if (TryReadAbgr(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu", out c)) return c;

            try { return (Color)ColorConverter.ConvertFromString(FallbackHex)!; }
            catch { return Colors.DeepSkyBlue; }
        }

        // 레지스트리에는 0xAABBGGRR(ABGR) 순서의 DWORD로 들어있다 — RGB와 바이트 순서가 반대라
        // 그냥 읽으면 빨강과 파랑이 뒤바뀐다.
        private static bool TryReadAbgr(string subKey, string valueName, out Color color)
        {
            color = default;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey);
                if (key?.GetValue(valueName) is not int raw) return false;

                uint v = unchecked((uint)raw);
                color = Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
                return true;
            }
            catch { return false; }
        }
    }
}
