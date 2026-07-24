using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace KrisZone
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, _) => DragMove();
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"버전 {ver?.Major}.{ver?.Minor}.{ver?.Build}";
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://regina25846-code.github.io/K-zone/") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
