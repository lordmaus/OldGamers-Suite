using System;
using System.Reflection;
using System.Diagnostics;
using System.Windows;

namespace SuperMagnifier
{
    public partial class WhatsNewDialog : Window
    {
        public WhatsNewDialog()
        {
            InitializeComponent();
            string ver = FileVersionInfo.GetVersionInfo(
                Environment.ProcessPath ?? AppContext.BaseDirectory).ProductVersion ?? "Unknown";
            VersionLabel.Text = $"Version {ver}";
        }

        private void GotItBtn_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("SMLVWhatsNew", 0, 0);
            if (w >= 100 && h >= 60)
            {
                SizeToContent = System.Windows.SizeToContent.Manual;
                Width = w; Height = h;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("SMLVWhatsNew", Width, Height);
            base.OnClosed(e);
        }
    }
}