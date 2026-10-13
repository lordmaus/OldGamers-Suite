using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

#nullable enable


namespace FileCopy
{
    public partial class App : Application
    {
        public static string? PendingSourcePath { get; private set; }
        public static string? PendingDestPath   { get; private set; }
        public static bool   PendingAutoScan   { get; private set; }
        public static string? PendingJobPath    { get; private set; }

        public static void ClearPending()
        {
            PendingSourcePath = null;
            PendingDestPath   = null;
            PendingAutoScan   = false;
            PendingJobPath    = null;
        }

        /// <summary>
        /// True when the OS "Animate controls and elements" preference is off
        /// (Settings → Accessibility → Visual effects → Show animations).
        /// Gate any Storyboard / transition on this flag — WCAG 3.0 draft prep.
        /// </summary>
        public static bool ReduceMotion { get; private set; }
            = !System.Windows.SystemParameters.ClientAreaAnimation;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AppLogger.Initialize();
            // FC-05: every unhandled exception leaves a log line (file logger writes synchronously).
            DispatcherUnhandledException               += OnDispatcherUnhandled;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
            TaskScheduler.UnobservedTaskException      += OnUnobservedTask;
            ParseArgs(e.Args);

            var initialArgs = new InstanceArgs(PendingSourcePath, PendingDestPath, PendingAutoScan, PendingJobPath);
            if (!SingleInstance.TryAcquire(initialArgs))
            {
                // Another instance got the args — exit quietly
                Shutdown();
                return;
            }

            AppPrefs.Instance.Load();
            ApplyOsTextScaleIfDefault(); // m1: SC 1.4.4 — respect Windows "Make text bigger"
            SetAppFontFamily(AppPrefs.Instance.AppFontFamily);
            SetAppFontSize(AppPrefs.Instance.AppFontSize);
            AppPrefs.Instance.EnableAutoSave();
            AppTheme.Apply();
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast)) AppTheme.Apply();
                // WCAG 3.0 draft — reduce-motion: update flag when OS setting changes at runtime
                if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                    ReduceMotion = !SystemParameters.ClientAreaAnimation;
            };
            AppTheme.StartAutoWatch();
            SingleInstance.SecondaryLaunched += OnSecondaryLaunched;

            // Surface portable mode clearly in startup log
            if (PortableMode.Active)
                AppLogger.ForContext<App>().LogInformation("Running in portable mode. Data dir: {Dir}", PortableMode.DataDir);

            // StartupUri removed — App.xaml is in src/ so the XAML-relative URI can't
            // resolve MainWindow.xaml at src/UI/. Create the window explicitly instead.
            new MainWindow().Show();
        }

        // ── FC-05 crash handling ──────────────────────────────────────────────
        private static int _fatalShown;

        private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLogger.ForContext<App>().LogCritical(e.Exception, "Unhandled UI exception");
            e.Handled = true;
            if (Interlocked.Exchange(ref _fatalShown, 1) != 0) return;
            TrySavePrefs();
            var r = MessageBox.Show(
                "File Copy Studio hit an unexpected error and has to close.\n\n" +
                $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
                "Details were written to the log. Interrupted copies resume on the next run.\n\n" +
                "Open the log folder?",
                "File Copy Studio", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);
            if (r == MessageBoxResult.Yes) OpenLogFolder();
            Shutdown(1);
        }

        private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            AppLogger.ForContext<App>().LogCritical(e.ExceptionObject as Exception,
                "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            TrySavePrefs();
        }

        private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogger.ForContext<App>().LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        }

        private static void TrySavePrefs()
        {
            try { AppPrefs.Instance.Save(); } catch { /* crash path — never throw */ }
        }

        internal static void OpenLogFolder()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(PortableMode.LogsDir);
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { AppLogger.ForContext<App>().LogWarning(ex, "Open log folder failed"); }
        }

        public static void ForceApplyTheme() => AppTheme.Apply();
        public static bool SystemIsDark()    => AppTheme.SystemIsDark();

        /// <summary>
        /// Low-Vision support: swap the app-wide font family at runtime. All controls
        /// that reference {DynamicResource AppFontFamily} update immediately.
        /// </summary>
        public static void SetAppFontFamily(string family)
        {
            try { Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family); }
            catch { Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily("Segoe UI"); }
        }

        /// <summary>
        /// Low-Vision support: swap the app-wide body font size (clamped 8..72).
        /// Updates {DynamicResource UiBodyBase}.
        /// </summary>
        public static void SetAppFontSize(double size)
        {
            Current.Resources["UiBodyBase"] = Math.Clamp(size, 8, 72);
        }

        /// <summary>
        /// SC 1.4.4 (WCAG2ICT): if the user has set Windows "Make text bigger" and hasn't
        /// already overridden UiScale manually, auto-select the nearest scale step on first
        /// launch. SystemParameters.MessageFontSize is ~12 at 100%, scales linearly with
        /// the OS text-size setting (Accessibility → Text size).
        /// Only fires when saved scale is "Standard" (factory default) — never overrides
        /// an explicit user choice.
        /// </summary>
        private static void ApplyOsTextScaleIfDefault()
        {
            if (AppPrefs.Instance.UiScale != "Standard") return;

            double fontSize = SystemFonts.MessageFontSize; // ~12 at 100%
            string scale = fontSize switch
            {
                <= 12.5 => "Standard",    // 100 % — no change
                <= 14.0 => "Medium",      // ~115 %
                <= 16.0 => "Large",       // ~130 %
                _       => "Xtra Large"   // ≥150 %
            };

            if (scale != "Standard")
            {
                AppPrefs.Instance.UiScale = scale;
                AppLogger.ForContext<App>().LogInformation(
                    "OS text size {FontSize:F1}px → auto-selected UiScale '{Scale}'", fontSize, scale);
            }
        }

        private void OnSecondaryLaunched(InstanceArgs args)
        {
            if (Current?.MainWindow is not MainWindow mw) return;

            // Restore + activate
            if (mw.WindowState == WindowState.Minimized) mw.WindowState = WindowState.Normal;
            mw.Activate();
            mw.Topmost = true;
            mw.Topmost = false;
            mw.Focus();

            mw.ApplyExternalArgs(args);
        }

        /// <summary>
        /// Shell context-menu args:
        ///   --source &lt;path&gt;
        ///   --dest   &lt;path&gt;
        ///   --scan            auto-scan after launch
        ///   &lt;path&gt;        positional → source if folder, source-folder if file
        /// </summary>
        private static void ParseArgs(string[] args)
        {
            if (args == null || args.Length == 0) return;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                { PendingSourcePath = SanitizePath(args[++i]); }
                else if (string.Equals(a, "--dest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                { PendingDestPath = SanitizePath(args[++i]); }
                else if (string.Equals(a, "--scan", StringComparison.OrdinalIgnoreCase))
                { PendingAutoScan = true; }
                else if (!a.StartsWith("--"))
                {
                    string? p = SanitizePath(a);
                    if (p == null) continue;
                    if (Directory.Exists(p))                                               PendingSourcePath = p;
                    else if (File.Exists(p) && p.EndsWith(FcJob.Extension, StringComparison.OrdinalIgnoreCase)) PendingJobPath = p;
                    else if (File.Exists(p))                                               PendingSourcePath = Path.GetDirectoryName(p);
                }
            }
        }

        /// <summary>
        /// Basic sanitization for paths from untrusted sources (shell verbs, pipe forward).
        /// Rejects null, empty, overlong, and paths containing null bytes.
        /// </summary>
        private static string? SanitizePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw.Length > 4096)              return null;   // absurdly long
            if (raw.IndexOf('\0') >= 0)         return null;   // null-byte injection
            return raw.Trim('"', ' ');
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppTheme.StopAutoWatch();

            // Serialize here on the UI thread (FC-06); cap the write at 2 s — slow networked
            // %APPDATA% shouldn't block exit.
            try
            {
                string? json = AppPrefs.Instance.Snapshot();
                if (json != null)
                    Task.Run(() => AppPrefs.Instance.WriteJson(json)).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { AppLogger.ForContext<App>().LogWarning(ex, "OnExit save failed"); }

            SingleInstance.Release();
            AppLogger.Shutdown();
            base.OnExit(e);
        }
    }
}
