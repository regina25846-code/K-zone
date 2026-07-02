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

            SettingsManager.Load();
            MonitorManager.Refresh();

            _engine = new DragSnapEngine();
            _engine.Install();

            _hotkeys = new HotkeyEngine();
            _hotkeys.Install();

            BuildTray();

            if (SettingsManager.IsFirstRun)
                OpenOverlayEditor(MonitorManager.Monitors.Count > 0 ? MonitorManager.Monitors[0] : null);
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

            // One entry per monitor
            if (MonitorManager.Monitors.Count == 1)
            {
                menu.Items.Add("레이아웃 편집", null, (_, _) => OpenOverlayEditor(MonitorManager.Monitors[0]));
            }
            else
            {
                foreach (var m in MonitorManager.Monitors)
                {
                    var mon = m;
                    menu.Items.Add(mon.DisplayName + " 편집", null, (_, _) => OpenOverlayEditor(mon));
                }
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("설정", null, (_, _) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => { Current.Shutdown(); });

            _trayIcon = new NotifyIcon
            {
                Icon = GetIcon(),
                Text = "KrisZone",
                Visible = true,
                ContextMenuStrip = menu
            };
            // Double-click: open editor for monitor under cursor
            _trayIcon.DoubleClick += (_, _) =>
            {
                var m = MonitorManager.Monitors.Count > 0 ? MonitorManager.Monitors[0] : null;
                OpenOverlayEditor(m);
            };
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
