using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using KrisZone.Editor;
using KrisZone.Settings;

namespace KrisZone
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _trayIcon;
        private NativePopupMenu? _trayMenu;
        private DragSnapEngine? _engine;
        private HotkeyEngine? _hotkeys;
        private AlwaysOnTopEngine? _alwaysOnTop;
        private System.Threading.Mutex? _singleInstanceMutex;
        private LayoutBrowserWindow? _layoutBrowserWindow;
        private AboutWindow? _aboutWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 중복 실행 방지 — 두 인스턴스가 동시에 떠있으면 서로 다른 WinEvent 훅/SetWindowPos가
            // 충돌해서 존 배치가 흔들리는 등 예측 불가능한 버그가 생김.
            // ⚠️ 방향에 주의: 뮤텍스를 '못 만든 쪽'(= 나중에 뜬 쪽)이 스스로 물러난다. 먼저 떠 있던
            // 인스턴스는 절대 건드리지 않는다(2026-09-04 PND-0033 조사에서 방향 재확인).
            _singleInstanceMutex = new System.Threading.Mutex(true, "KrisZone_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                ExitLogger.MarkDuplicateInstance();
                Shutdown();
                return;
            }

            // 종료 사유 로그 — 정상 종료까지 포함해 '어떤 경로로 끝났는지'를 남긴다(PND-0033).
            // 예외가 났을 때만 남기던 crash.log만으로는 2026-09-04 사고에서 아무것도 못 잡았다.
            ExitLogger.BeginSession();

            // 윈도우 종료·로그오프. WPF는 이 이벤트 뒤 앱을 종료시키므로 사유를 먼저 남긴다.
            SessionEnding += (_, se) =>
                ExitLogger.Log("SESSION_ENDING", "윈도우",
                    $"윈도우 종료/로그오프 요청 (reason={se.ReasonSessionEnding})");

            // OnExit이 안 불리는 종료 경로(Environment.Exit 등)의 마지막 그물.
            // 외부 강제종료(TerminateProcess)에서는 이것도 안 불리는데, 그 경우 session.lock이
            // 남아 다음 실행 때 PREVIOUS_RUN_NO_EXIT_RECORD로 잡힌다.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                ExitLogger.EndSession("PROCESS_EXIT", "앱 내부", "CLR 프로세스 종료");

            // 크래시 로그
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                ExitLogger.Log("UNHANDLED_EXCEPTION", "앱 내부", "처리되지 않은 예외 — 상세는 crash.log");
                WriteCrashLog(ex.ExceptionObject?.ToString() ?? "Unknown");
            };
            DispatcherUnhandledException += (_, ex) =>
            {
                // 이 경로는 Handled=true라 종료로 이어지지 않는다(계속 실행됨).
                ExitLogger.Log("DISPATCHER_EXCEPTION", "앱 내부", "UI 스레드 예외 — 삼키고 계속 실행, 상세는 crash.log");
                WriteCrashLog(ex.Exception?.ToString() ?? "Unknown");
                ex.Handled = true;
            };

            SettingsManager.Load();
            MonitorManager.Refresh();

            _engine = new DragSnapEngine();
            _engine.Install();

            _alwaysOnTop = new AlwaysOnTopEngine();
            _alwaysOnTop.Install();

            _hotkeys = new HotkeyEngine();
            _hotkeys.Install();

            if (SettingsManager.IsFirstRun)
                SetAutoStart(true);
            else if (IsAutoStartEnabled())
                SetAutoStart(true); // 경로가 바뀌었을 경우 갱신

            // 옛 이름(K-FancyZones) 시작 프로그램 잔재 정리 — 위 등록/갱신 뒤에 돌려야
            // '새 이름 등록이 있는지'를 최신 상태로 판단한다.
            CleanupLegacyAutoStart();

            BuildTray();

            if (SettingsManager.IsFirstRun)
                OpenLayoutBrowser();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 정리 작업이 예외로 터져도 종료 기록은 남도록 맨 앞에서 기록한다.
            ExitLogger.EndSession("APP_EXIT", "앱 내부", $"WPF 종료 완료 (exitCode={e.ApplicationExitCode})");

            _engine?.Dispose();
            _hotkeys?.Dispose();
            _alwaysOnTop?.Dispose();
            _trayIcon?.Dispose();
            _trayMenu?.Dispose();
            base.OnExit(e);
        }

        private static string? GetExePath() =>
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath;

        private static string StartupShortcutPath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "K-Zone.lnk");

        private static void SetAutoStart(bool enable)
        {
            // 레지스트리 방식
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key != null)
                {
                    if (enable)
                    {
                        var path = GetExePath();
                        if (!string.IsNullOrEmpty(path))
                            key.SetValue("K-Zone", $"\"{path}\"");
                    }
                    else
                        key.DeleteValue("K-Zone", throwOnMissingValue: false);
                }
            }
            catch { }

            // Windows 시작 앱 승인 키 (설정 앱 토글과 연동)
            try
            {
                using var approved = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                if (approved != null)
                {
                    // 03으로 시작하면 활성, 01이면 비활성
                    var val = enable
                        ? new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                        : new byte[] { 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    approved.SetValue("K-Zone", val, RegistryValueKind.Binary);
                }
            }
            catch { }

            // 시작 폴더 단축키 방식 (레지스트리보다 더 확실)
            try
            {
                var lnk = StartupShortcutPath;
                if (enable)
                {
                    var exePath = GetExePath();
                    if (string.IsNullOrEmpty(exePath)) return;
                    var ps = $"$s=$((New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}'));$s.TargetPath='{exePath}';$s.Save()";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{ps}\"",
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    })?.WaitForExit(3000);
                }
                else if (System.IO.File.Exists(lnk))
                    System.IO.File.Delete(lnk);
            }
            catch { }
        }

        private static bool IsAutoStartEnabled()
        {
            if (System.IO.File.Exists(StartupShortcutPath)) return true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run");
                return key?.GetValue("K-Zone") != null;
            }
            catch { return false; }
        }

        // 앱 이름이 K-FancyZones → K-Zone 으로 바뀐 건 v1.0.8(2026-07-04, 커밋 9a31325)인데,
        // 그때 '새 이름으로 등록'만 하고 '옛 이름 등록 제거'는 안 했다. 그래서 그 이전부터 쓰던
        // 사용자의 시작 프로그램 목록에는 K-FancyZones 항목이 계속 남아 있다(형 PC 실제 확인,
        // k-fancyzone / k-zone 두 개). 설치 제거 스크립트(setup.iss)도 K-Zone 이름만 지운다.
        //
        // ⚠️ 안전장치: 새 이름(K-Zone) 등록이 실제로 있을 때만 옛 이름을 지운다.
        // 만약 사용자의 자동시작이 옛 항목 하나뿐인 상태에서 그걸 지워버리면 자동시작 기능
        // 자체가 조용히 꺼져버린다. 그 경우엔 지우지 않고 기록만 남긴다.
        private static void CleanupLegacyAutoStart()
        {
            const string LegacyName = "K-FancyZones";
            try
            {
                var legacyLnk = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{LegacyName}.lnk");

                bool hasLegacyLnk = System.IO.File.Exists(legacyLnk);
                bool hasLegacyRun = false;
                try
                {
                    using var probe = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run");
                    hasLegacyRun = probe?.GetValue(LegacyName) != null;
                }
                catch { }

                bool hasLegacyApproved = false;
                try
                {
                    using var probe = Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                    hasLegacyApproved = probe?.GetValue(LegacyName) != null;
                }
                catch { }

                if (!hasLegacyLnk && !hasLegacyRun && !hasLegacyApproved) return;

                if (!IsAutoStartEnabled())
                {
                    ExitLogger.Log("LEGACY_AUTOSTART_KEPT", "앱 내부",
                        $"옛 이름({LegacyName}) 시작 프로그램 등록을 발견했으나, 새 이름(K-Zone) 등록이 없어 " +
                        "자동시작이 통째로 꺼지는 것을 막기 위해 지우지 않고 남겨둠");
                    return;
                }

                var removed = new System.Collections.Generic.List<string>();

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                    if (key?.GetValue(LegacyName) != null)
                    {
                        key.DeleteValue(LegacyName, throwOnMissingValue: false);
                        removed.Add("Run 레지스트리");
                    }
                }
                catch { }

                try
                {
                    using var approved = Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", writable: true);
                    if (approved?.GetValue(LegacyName) != null)
                    {
                        approved.DeleteValue(LegacyName, throwOnMissingValue: false);
                        removed.Add("StartupApproved 레지스트리");
                    }
                }
                catch { }

                try
                {
                    if (System.IO.File.Exists(legacyLnk))
                    {
                        System.IO.File.Delete(legacyLnk);
                        removed.Add("시작 폴더 단축키");
                    }
                }
                catch { }

                if (removed.Count > 0)
                    ExitLogger.Log("LEGACY_AUTOSTART_REMOVED", "앱 내부",
                        $"옛 이름({LegacyName}) 시작 프로그램 잔재 제거: {string.Join(", ", removed)}");
            }
            catch { }
        }

        // 트레이 메뉴에서 자동시작을 켜고 끄는 경로. 누른 뒤 실제 상태를 다시 읽어 확인하고
        // 풍선 알림으로 결과를 알려준다(예전엔 아무 반응이 없어서 눌렸는지도 알 수 없었다 — PND-0110).
        private void ToggleAutoStart()
        {
            bool before = IsAutoStartEnabled();
            SetAutoStart(!before);
            bool after = IsAutoStartEnabled();

            string message;
            if (after == before)
                message = after
                    ? "설정을 바꾸지 못했습니다. 여전히 '켜짐' 상태입니다."
                    : "설정을 바꾸지 못했습니다. 여전히 '꺼짐' 상태입니다.";
            else
                message = after
                    ? "이제 컴퓨터를 켤 때 K-Zone이 자동으로 실행됩니다."
                    : "이제 컴퓨터를 켤 때 K-Zone이 자동으로 실행되지 않습니다.";

            ExitLogger.Log("AUTOSTART_TOGGLED", "사용자 클릭",
                $"시작 프로그램 등록: {(before ? "켜짐" : "꺼짐")} → {(after ? "켜짐" : "꺼짐")}");

            try
            {
                _trayIcon?.ShowBalloonTip(4000,
                    after ? "시작 프로그램 등록: 켜짐" : "시작 프로그램 등록: 꺼짐",
                    message,
                    after == before ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch { }
        }

        private void BuildTray()
        {
            _trayMenu = new NativePopupMenu();

            _trayIcon = new NotifyIcon
            {
                Icon = GetTrayIcon(),
                Text = "K-Zone",
                Visible = true,
            };
            _trayIcon.DoubleClick += (_, _) => OpenLayoutBrowser();
            _trayIcon.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Right) return;

                // 체크 표시만으로는 형이 못 알아봤다(2026-09-04 PND-0110 — "설정이 없어진 줄 알았다").
                // 체크 표시에 더해 글자로도 현재 상태를 같이 보여준다.
                bool autoStartOn = IsAutoStartEnabled();
                // 글자는 일부러 기호 없이 한글로만 쓴다 — 트레이 메뉴는 OS가 시스템 글꼴로 그리는
                // 네이티브 메뉴라, 특수기호는 글꼴에 따라 네모로 깨질 수 있다.
                string autoStartLabel = autoStartOn
                    ? "시작 프로그램 등록 (켜짐)"
                    : "시작 프로그램 등록 (꺼짐)";

                _trayMenu.Show(new[]
                {
                    NativePopupMenu.Item.Entry("K-Zone 레이아웃 편집기", OpenLayoutBrowser),
                    NativePopupMenu.Item.Separator(),
                    NativePopupMenu.Item.Entry(autoStartLabel, ToggleAutoStart, autoStartOn),
                    NativePopupMenu.Item.Separator(),
                    NativePopupMenu.Item.Entry("프로그램 정보", OpenAboutWindow),
                    NativePopupMenu.Item.Separator(),
                    NativePopupMenu.Item.Entry("종료", () =>
                    {
                        ExitLogger.Log("TRAY_MENU_EXIT", "사용자 클릭", "트레이 메뉴에서 '종료'를 누름");
                        Current.Shutdown();
                    }),
                });
            };
        }

        private void OpenAboutWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_aboutWindow != null)
                {
                    if (_aboutWindow.WindowState == WindowState.Minimized)
                        _aboutWindow.WindowState = WindowState.Normal;
                    _aboutWindow.Activate();
                    return;
                }
                _aboutWindow = new AboutWindow();
                _aboutWindow.Closed += (_, _) => _aboutWindow = null;
                _aboutWindow.Show();
                _aboutWindow.Activate();
            });
        }

        private void OpenLayoutBrowser()
        {
            // 트레이 아이콘 클릭할 때마다 매번 새 창을 만들어서 계속 누르면 창이
            // 무한정 쌓였다(2026-08-04 형이 실제 재현) — 이미 열려있으면 그 창을
            // 앞으로 가져오기만 하도록 수정.
            Dispatcher.Invoke(() =>
            {
                if (_layoutBrowserWindow != null)
                {
                    if (_layoutBrowserWindow.WindowState == WindowState.Minimized)
                        _layoutBrowserWindow.WindowState = WindowState.Normal;
                    _layoutBrowserWindow.Activate();
                    return;
                }
                _layoutBrowserWindow = new LayoutBrowserWindow();
                _layoutBrowserWindow.Closed += (_, _) => _layoutBrowserWindow = null;
                _layoutBrowserWindow.Show();
                _layoutBrowserWindow.Activate();
            });
        }

        private void OpenOverlayEditor(MonitorInfo? monitor)
        {
            if (monitor == null) return;
            Dispatcher.Invoke(() =>
            {
                // 편집기는 앱 전체에서 하나만 뜬다 — 이미 떠 있으면 그 창을 앞으로.
                if (MonitorOverlayEditor.TryActivateExisting()) return;

                var w = new MonitorOverlayEditor(monitor);
                w.Show();
                w.Activate();
            });
        }

        private static void WriteCrashLog(string content)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-Zone");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "crash.log");
                System.IO.File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{content}\n\n");
            }
            catch { }
        }

        private static Icon GetTrayIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/tray_icon.png");
                var stream = GetResourceStream(uri)?.Stream;
                if (stream != null)
                    return PngToIcon(stream, 32);
            }
            catch { }
            return GetMainIcon();
        }

        private static Icon GetMainIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/icon.ico");
                var stream = GetResourceStream(uri)?.Stream;
                if (stream != null) return new Icon(stream);
            }
            catch { }
            return SystemIcons.Application;
        }

        private static Icon PngToIcon(System.IO.Stream pngStream, int size)
        {
            using var src = System.Drawing.Image.FromStream(pngStream);
            using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            // GetHicon()이 만든 HICON은 Icon.FromHandle이 소유권을 안 가져가므로, 독립적인
            // 복제본(Clone)을 반환하고 원본 HICON은 DestroyIcon으로 즉시 해제해 GDI 핸들 누수 방지
            IntPtr hicon = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(hicon);
                return (Icon)tmp.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(hicon);
            }
        }
    }
}
