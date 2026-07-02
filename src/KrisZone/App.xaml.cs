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
            menu.Items.Add("편집기 열기", null, (_, _) => OpenEditor());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => { Current.Shutdown(); });

            _trayIcon = new NotifyIcon
            {
                Icon = GetIcon(),
                Text = "KrisZone",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.DoubleClick += (_, _) => OpenEditor();
        }

        private void OpenEditor()
        {
            Dispatcher.Invoke(() =>
            {
                var w = new EditorWindow();
                w.Show();
                w.Activate();
            });
        }

        private static Icon GetIcon()
        {
            // Embedded icon fallback: create simple icon from resources
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
