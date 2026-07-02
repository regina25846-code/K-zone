using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
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

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("K-FancyZones 레이아웃 편집기", null, (_, _) => OpenLayoutBrowser());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => { Current.Shutdown(); });

            _trayIcon = new NotifyIcon
            {
                Icon = GetIcon(),
                Text = "K-FancyZones",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.DoubleClick += (_, _) => OpenLayoutBrowser();
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

        private static Icon GetIcon()
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
    }
}
