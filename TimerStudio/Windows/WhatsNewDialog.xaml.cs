using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TimerStudio
{
    public partial class WhatsNewDialog : Window
    {
        private static readonly string SeenFile = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "TimerStudio", "seen_version.txt");

        public static string CurrentVersion => VersionInfo.Display;

        /// <summary>
        /// Show the dialog only if this version hasn't been seen before.
        /// </summary>
        public static void ShowIfNewVersion(Window owner)
        {
            try
            {
                string seen = File.Exists(SeenFile) ? File.ReadAllText(SeenFile).Trim() : "";
                if (seen == CurrentVersion) return;
                new WhatsNewDialog { Owner = owner }.ShowDialog();
                Directory.CreateDirectory(Path.GetDirectoryName(SeenFile));
                File.WriteAllText(SeenFile, CurrentVersion);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine("[TimerStudio] WhatsNew check failed: " + ex.Message);
            }
        }

        public WhatsNewDialog()
        {
            InitializeComponent();
            LblVersion.Text = "Version " + CurrentVersion;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("TSWhatsNew", 0, 0);
            if (w >= 100 && h >= 60)
            {
                SizeToContent = System.Windows.SizeToContent.Manual;
                Width = w; Height = h;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("TSWhatsNew", Width, Height);
            base.OnClosed(e);
        }
    }
}