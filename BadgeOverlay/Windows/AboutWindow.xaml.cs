using System;
using System.Windows;

namespace BadgeOverlay
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TrySetIcon();

            // Version — single source via VersionInfo.
            if (VersionLabel != null)
                VersionLabel.Text = VersionInfo.Display;

            // Settings path — GetDefaultsPath() returns the portable-aware path
            // (next to EXE in portable mode, %APPDATA%\BadgeOverlay otherwise).
            if (LblSettingsPath != null)
                LblSettingsPath.Text = AppSettings.GetDefaultsPath();

        }

        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            // Confirm hard, twice — accidental reset would lose every
            // custom rule, every hotkey, every colour preference. We
            // back up to a timestamped .bak so a misclick is recoverable
            // by importing the backup.
            var r = AppDialog.Show(
                "Reset ALL settings to factory defaults?\n\n" +
                "Your current settings will be backed up to settings.json.bak-<timestamp> first. " +
                "You can restore them with About → Import.\n\n" +
                "The app will restart after reset.",
                "Reset all settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            try
            {
                string p = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BadgeOverlay", "settings.json");
                if (System.IO.File.Exists(p))
                {
                    string bak = p + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    System.IO.File.Move(p, bak);
                }
                // App.Restart releases the single-instance mutex, starts the new process with
                // --restart <pid> (it waits for us to exit) and shuts down — no UI-thread sleep.
                App.Restart(discardSettings: true);
            }
            catch (Exception ex)
            {
                AppLog.Error("BtnResetAll_Click", ex);
                AppDialog.Show(
                    "Reset failed. See About → View log for details.",
                    "Reset all settings",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TrySetIcon()
        {
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(p))
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(p, UriKind.Absolute));
            }
            catch { }
        }

        private void BtnCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(LblSettingsPath?.Text ?? ""); }
            catch { }
        }

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            // Settings export lives on MainWindow (it owns the live AppSettings
            // instance and the JSON source-gen context). About is just a
            // facade — delegate so behaviour stays consistent regardless of
            // which dialog the user clicks Export from.
            try
            {
                if (Owner is MainWindow mw) mw.ExportSettingsPublic();
            }
            catch (Exception ex) { AppLog.Error("BtnExportSettings_Click", ex); }
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Owner is MainWindow mw) mw.ImportSettingsPublic();
            }
            catch (Exception ex) { AppLog.Error("BtnImportSettings_Click", ex); }
        }

        private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://github.com/lordmaus/Badge-Overlay/releases",
                UseShellExecute = true
            }); }
            catch { }
        }

        private void BtnOpenReadme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Under PublishSingleFile=true, AppContext.BaseDirectory
                // points at the per-user temp extraction folder
                // (%TEMP%\.net\BadgeOverlay\<hash>\) — the README is NOT
                // there. The installer drops README.md into the install
                // directory next to the EXE, so we resolve the path
                // from Process.MainModule.FileName instead.
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                string installDir = !string.IsNullOrEmpty(exe)
                    ? System.IO.Path.GetDirectoryName(exe)
                    : AppDomain.CurrentDomain.BaseDirectory;

                string readme = System.IO.Path.Combine(installDir, "README.md");
                if (!System.IO.File.Exists(readme))
                {
                    // Fallback for dev / xcopy runs where README sits
                    // alongside the project, not the EXE.
                    string altReadme = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "README.md");
                    if (System.IO.File.Exists(altReadme)) readme = altReadme;
                }

                if (System.IO.File.Exists(readme))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = readme,
                        UseShellExecute = true
                    });
                }
                else
                {
                    // Last-ditch: open the GitHub-hosted README so the
                    // user isn't left with a dead button. URL is the
                    // canonical project page; harmless if offline (the
                    // shell will show a normal "no internet" dialog).
                    AppDialog.Show(
                        "README.md was not found in the install folder. " +
                        "Opening the online version instead.",
                        "Help",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName        = "https://github.com/lordmaus/Badge-Overlay#readme",
                            UseShellExecute = true
                        });
                    }
                    catch { /* swallow: no browser */ }
                }
            }
            catch (Exception ex) { AppLog.Error("BtnOpenReadme_Click", ex); }
        }

        /// <summary>
        /// Opens the keyboard shortcut cheat-sheet (also reachable via
        /// F1 from MainWindow). Owner is set to this About dialog so
        /// the cheat-sheet centers and modally blocks About; About
        /// itself remains modal to MainWindow.
        /// </summary>
        private void BtnCheatSheet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prefer the live settings from MainWindow so the cheat-sheet
                // reflects any in-session changes without requiring a disk round-trip.
                AppSettings s = (Owner as MainWindow)?.Settings;
                var win = new CheatSheetWindow(s) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex) { AppLog.Error("BtnCheatSheet_Click", ex); }
        }

        /// <summary>
        /// Re-opens the first-run welcome tour. Distinct from the
        /// auto-trigger in MainWindow.OnLoaded — this path doesn't
        /// touch FirstRunWelcomed regardless of completion, because
        /// the user explicitly asked to see it again. Setting it back
        /// to false would make the welcome auto-trigger on next launch
        /// too, which is not what "show me again now" means.
        /// </summary>
        private void BtnReplayWelcome_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var welcome = new WelcomeWindow { Owner = this };
                welcome.ShowDialog();
            }
            catch (Exception ex) { AppLog.Error("BtnReplayWelcome_Click", ex); }
        }

        /// <summary>
        /// Runs an accessibility audit against whichever window is
        /// foregrounded after a brief countdown. The countdown gives
        /// the user time to switch focus to the app they actually
        /// want audited — without it, the audit would always target
        /// About itself, which is not useful.
        ///
        /// Asks for a save path up front (so an audit-before-save
        /// failure isn't silently lost), then minimises About,
        /// counts down, runs the walk, restores About, opens the
        /// generated HTML in the default browser.
        /// </summary>
        private void BtnAudit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ask for the save target first. SaveFileDialog has its
                // own owner; About staying open behind it is fine.
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title    = "Save Accessibility Audit",
                    Filter   = "HTML report (*.html)|*.html",
                    FileName = $"a11y-audit-{DateTime.Now:yyyy-MM-dd-HHmm}.html",
                    AddExtension = true,
                };
                if (dlg.ShowDialog(this) != true) return;
                string outPath = dlg.FileName;

                // Minimise About so the user can switch focus without
                // having to manually move it. Restore after the audit.
                var prevState = WindowState;
                WindowState = WindowState.Minimized;

                // 3-second countdown — Dispatcher-based so the UI
                // thread stays responsive. The countdown lives in a
                // small notice window the user can see; we don't want
                // an audit to fire silently and feel "magical".
                var notice = new Window
                {
                    Title = "Audit countdown",
                    Width = 380, Height = 170,
                    WindowStyle = WindowStyle.ToolWindow,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    Background = (System.Windows.Media.Brush)FindResource("BrushBg"),
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushText"),
                };
                var stack = new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(16),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var headlineLbl = new System.Windows.Controls.TextBlock
                {
                    Text = "Auditing in 3…",
                    TextAlignment = TextAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                var targetLbl = new System.Windows.Controls.TextBlock
                {
                    Text = "Target: (resolving…)",
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushMuted"),
                };
                stack.Children.Add(headlineLbl);
                stack.Children.Add(targetLbl);
                notice.Content = stack;
                notice.Show();

                // Live foreground preview during countdown so the user
                // can verify which window will actually be audited.
                // Updated every 200 ms — frequent enough to feel
                // responsive when the user Alt-Tabs but not so frequent
                // that we hammer GetForegroundWindow.
                //
                // The notice window itself can never be the foreground
                // target: it's a ToolWindow with Topmost=true and we
                // skip it explicitly when computing the live preview.
                var noticeHandle = new System.Windows.Interop.WindowInteropHelper(notice).EnsureHandle();
                var aboutHandle  = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr lastForeground = IntPtr.Zero;

                var preview = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                preview.Tick += (ps, pe) =>
                {
                    try
                    {
                        IntPtr fg = NativeMethods.GetForegroundWindow();
                        // Ignore self windows — About may briefly retake
                        // foreground during minimise, the notice itself
                        // is topmost.
                        if (fg == IntPtr.Zero || fg == noticeHandle || fg == aboutHandle)
                            return;
                        if (fg == lastForeground) return;
                        lastForeground = fg;
                        string title = NativeMethods.GetText(fg);
                        if (string.IsNullOrWhiteSpace(title)) title = "(untitled window)";
                        targetLbl.Text = $"Target: {title}";
                    }
                    catch { /* swallow: best-effort preview */ }
                };
                preview.Start();

                int tick = 3;
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (ts, te) =>
                {
                    tick--;
                    if (tick > 0)
                    {
                        headlineLbl.Text = $"Auditing in {tick}…";
                        return;
                    }
                    timer.Stop();
                    preview.Stop();
                    notice.Close();

                    // Run the audit and save the report. UIA walk is
                    // blocking but bounded by A11yAuditor.MaxElements.
                    // Pass the captured handle so the audit targets
                    // exactly what the live preview showed — without
                    // this, the gap between notice.Close() and the
                    // audit call could let a transient foreground swap
                    // (e.g. notification toast) hijack the target.
                    A11yAuditor.AuditResult result = null;
                    try
                    {
                        result = A11yAuditor.AuditWindow(
                            lastForeground != IntPtr.Zero
                                ? lastForeground
                                : NativeMethods.GetForegroundWindow());
                        A11yAuditor.WriteHtmlReport(result, outPath);
                    }
                    catch (Exception ex2)
                    {
                        AppLog.Error("BtnAudit_Click run", ex2);
                        WindowState = prevState;
                        AppDialog.Show(
                            "The audit failed. Details have been written to the log.",
                            "Audit failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    WindowState = prevState;

                    // Open the report in the default browser. If it
                    // fails (no default association), surface a hint.
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName        = outPath,
                                UseShellExecute = true
                            });
                    }
                    catch (Exception ex3)
                    {
                        AppLog.Error("BtnAudit_Click open", ex3);
                        AppDialog.Show(
                            $"Audit saved to:\n{outPath}\n\n" +
                            "Couldn't open it automatically — open it manually in your browser.",
                            "Audit complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                };
                timer.Start();
            }
            catch (Exception ex) { AppLog.Error("BtnAudit_Click", ex); }
        }

        private void BtnVenmo_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://venmo.com/",
                UseShellExecute = true
            }); }
            catch { }
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://oldgamerssoftware.com/",
                UseShellExecute = true
            }); }
            catch (Exception ex) { AppLog.Error("BtnWebsite_Click", ex); }
        }

        private void BtnGitHub_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://github.com/lordmaus/Badge-Overlay",
                UseShellExecute = true
            }); }
            catch (Exception ex) { AppLog.Error("BtnGitHub_Click", ex); }
        }

        private void BtnThirdPartyNotices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess()
                    .MainModule?.FileName ?? "";
                string dir  = string.IsNullOrEmpty(exe)
                    ? AppContext.BaseDirectory
                    : System.IO.Path.GetDirectoryName(exe);
                string path = System.IO.Path.Combine(dir ?? "", "THIRD-PARTY-NOTICES.txt");

                if (System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = path,
                        UseShellExecute = true
                    });
                }
                else
                {
                    AppDialog.Show(
                        $"THIRD-PARTY-NOTICES.txt not found at:\n{path}\n\n" +
                        "The file should be installed alongside BadgeOverlay.exe.",
                        "File not found", System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex) { AppLog.Error("BtnThirdPartyNotices_Click", ex); }
        }

        private void BtnCashApp_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "https://cash.app/",
                UseShellExecute = true
            }); }
            catch { }
        }

        private void BtnReportProblem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Pre-fills the issue title + body with OS version and app
                // version so reporters don't have to look it up. No telemetry
                // is sent — this just opens the browser with a URL; all data
                // is visible to the user before they submit.
                string appVer = VersionInfo.DisplayBare;
                string osVer  = Environment.OSVersion.VersionString;
                string body   = System.Uri.EscapeDataString(
                    $"**Badge Overlay version:** {appVer}\n" +
                    $"**Windows version:** {osVer}\n\n" +
                    "**What happened?**\n\n\n" +
                    "**What did you expect to happen?**\n\n\n" +
                    "**Steps to reproduce**\n1. \n2. \n3. \n\n" +
                    "**Log tail** (optional — from About → View log)\n```\n\n```\n");
                string url = $"https://github.com/lordmaus/Badge-Overlay/issues/new?title=&body={body}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { AppLog.Error("BtnReportProblem_Click", ex); }
        }

        private void BtnViewCrashLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var w = new CrashLogWindow { Owner = this };
                w.ShowDialog();
            }
            catch (Exception ex) { AppLog.Error("BtnViewCrashLog_Click", ex); }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("BadgeAbout", 540, 640);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("BadgeAbout", Width, Height);
            base.OnClosed(e);
        }
    }
}