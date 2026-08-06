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
        private SettingsWindow? _settingsWindow;
        private AboutWindow? _aboutWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 중복 실행 방지 — 두 인스턴스가 동시에 떠있으면 서로 다른 WinEvent 훅/SetWindowPos가
            // 충돌해서 존 배치가 흔들리는 등 예측 불가능한 버그가 생김
            _singleInstanceMutex = new System.Threading.Mutex(true, "KrisZone_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            // 크래시 로그
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
                WriteCrashLog(ex.ExceptionObject?.ToString() ?? "Unknown");
            DispatcherUnhandledException += (_, ex) =>
            {
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

            BuildTray();

            if (SettingsManager.IsFirstRun)
                OpenLayoutBrowser();
        }

        protected override void OnExit(ExitEventArgs e)
        {
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
                _trayMenu.Show(new[]
                {
                    NativePopupMenu.Item.Entry("K-Zone 레이아웃 편집기", OpenLayoutBrowser),
                    NativePopupMenu.Item.Entry("설정", OpenSettings),
                    NativePopupMenu.Item.Separator(),
                    NativePopupMenu.Item.Entry("시작 프로그램 등록", () => SetAutoStart(!IsAutoStartEnabled()), IsAutoStartEnabled()),
                    NativePopupMenu.Item.Separator(),
                    NativePopupMenu.Item.Entry("프로그램 정보", OpenAboutWindow),
                    NativePopupMenu.Item.Separator(),
                    NativePopupMenu.Item.Entry("종료", () => Current.Shutdown()),
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
                var w = new MonitorOverlayEditor(monitor);
                w.Show();
                w.Activate();
            });
        }

        private void OpenSettings()
        {
            Dispatcher.Invoke(() =>
            {
                if (_settingsWindow != null)
                {
                    if (_settingsWindow.WindowState == WindowState.Minimized)
                        _settingsWindow.WindowState = WindowState.Normal;
                    _settingsWindow.Activate();
                    return;
                }
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Show();
                _settingsWindow.Activate();
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
