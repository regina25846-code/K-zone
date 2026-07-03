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
        private DragSnapEngine? _engine;
        private HotkeyEngine? _hotkeys;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            _trayIcon?.Dispose();
            base.OnExit(e);
        }

        private static string? GetExePath() =>
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath;

        private static string StartupShortcutPath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "K-FancyZones.lnk");

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
                            key.SetValue("K-FancyZones", $"\"{path}\"");
                    }
                    else
                        key.DeleteValue("K-FancyZones", throwOnMissingValue: false);
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
                return key?.GetValue("K-FancyZones") != null;
            }
            catch { return false; }
        }

        private void BuildTray()
        {
            var autoStartItem = new ToolStripMenuItem("시작 프로그램 등록")
            {
                Checked = IsAutoStartEnabled(),
                CheckOnClick = true
            };
            autoStartItem.Click += (_, _) => SetAutoStart(autoStartItem.Checked);

            var menu = new ContextMenuStrip();
            menu.Items.Add("K-FancyZones 레이아웃 편집기", null, (_, _) => OpenLayoutBrowser());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("프로그램 정보", null, (_, _) => OpenAboutWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => { Current.Shutdown(); });

            _trayIcon = new NotifyIcon
            {
                Icon = GetTrayIcon(),
                Text = "K-FancyZones",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.DoubleClick += (_, _) => OpenLayoutBrowser();
        }

        private void OpenAboutWindow()
        {
            Dispatcher.Invoke(() =>
            {
                var w = new AboutWindow();
                w.Show();
                w.Activate();
            });
        }

        private void OpenLayoutBrowser()
        {
            Dispatcher.Invoke(() =>
            {
                var w = new LayoutBrowserWindow();
                w.Show();
                w.Activate();
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
                var w = new SettingsWindow();
                w.Show();
                w.Activate();
            });
        }

        private static void WriteCrashLog(string content)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-FancyZones");
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
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
