using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BadgeOverlay
{
    public partial class App : Application
    {
        // C# 14: private-setter auto-property eliminates the _isDark backing field.
        // The field keyword is unnecessary here — no custom getter/setter logic —
        // so the idiomatic form is a plain auto-property with private set.
        public static bool IsDark { get; private set; } = true;

        // ── Single-instance mutex ─────────────────────────────────────────────
        private static Mutex  _instanceMutex;
        // Per-user single-instance mutex (Local\ rather than Global\) so
        // different Windows accounts on the same machine can each run their
        // own Badge Overlay instance. The name is NOT versioned: an in-place
        // update should still be detected as "already running" by the new
        // binary, so the user isn't asked to close an instance that's about
        // to be upgraded.
        private const  string MutexName = @"Local\BadgeOverlay_SingleInstance";

        // ── High-contrast monitor (WCAG 2.1 system-preference respect) ────────
        private static HighContrastMonitor _hcMonitor;
        public  static bool IsHighContrast => _hcMonitor?.IsHighContrast ?? false;

        // ── Theme switching ───────────────────────────────────────────────────
        // Light theme keys — every other key is treated as a dark variant.
        private static readonly System.Collections.Generic.HashSet<string> _lightThemeKeys =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            { "Light", "WarmLight", "CatppuccinLatte", "GitHubLight", "AyuLight", "SolarizedLight", "RosePineDawn", "EverforestLight", "PaperWhite", "Sepia", "Arctic", "Ivory", "HighContrastLight" };

        /// <summary>
        /// Returns true when <paramref name="themeName"/> is a dark-palette
        /// theme, false for the four light themes. Null/unknown defaults to dark.
        /// </summary>
        public static bool IsThemeDark(string themeName) =>
            !_lightThemeKeys.Contains(themeName ?? "Dark");

        /// <summary>
        /// Sets the user's preferred theme by name. Honoured only when
        /// Windows High-Contrast mode is OFF; if HC is active, the
        /// HighContrast theme takes precedence and this selection is
        /// remembered for when HC turns off again.
        /// </summary>
        public static void SetTheme(string themeName)
        {
            IsDark = IsThemeDark(themeName);
            _hcMonitor?.Sync(themeName);   // pass full key so HC→off restores exact theme
            if (_hcMonitor != null && _hcMonitor.IsHighContrast) return;
            ApplyTheme(themeName);
        }

        /// <summary>
        /// Legacy bool overload — kept for callers that pre-date the
        /// named-theme system. Delegates to <see cref="SetTheme(string)"/>.
        /// </summary>
        public static void SetTheme(bool dark) => SetTheme(dark ? "Dark" : "Light");

        /// <summary>
        /// Low-level theme loader. Valid keys: all theme names plus "HighContrast".
        /// Called by SetTheme and by HighContrastMonitor on OS state changes.
        /// </summary>
        private static string _themeKey = "Dark";
        public  static BadgeStyle CurrentBadgeStyle { get; private set; } = BadgeStyle.Dark;

        /// <summary>Settings → Badge style changed (or initial load).</summary>
        public static void SetBadgeStyle(BadgeStyle style)
        {
            CurrentBadgeStyle = style;
            ApplyOverlayPalette();
        }

        /// <summary>Re-publishes the Overlay* brushes for the current style / theme / HC state.</summary>
        public static void ApplyOverlayPalette()
        {
            try { OverlayPalette.Apply(CurrentBadgeStyle, _themeKey, SystemParameters.HighContrast); }
            catch (Exception ex) { CrashLog.Write("ApplyOverlayPalette", ex); }
        }

        public static void ApplyTheme(string key)
        {
            _themeKey = key ?? "Dark";
            string uri = key switch
            {
                "HighContrast"    => "Themes/HighContrast.xaml",
                "Light"           => "Themes/Light.xaml",
                "WarmLight"       => "Themes/WarmLight.xaml",
                "CatppuccinLatte" => "Themes/CatppuccinLatte.xaml",
                "GitHubLight"     => "Themes/GitHubLight.xaml",
                "OneDark"         => "Themes/OneDark.xaml",
                "GruvboxDark"     => "Themes/GruvboxDark.xaml",
                "TokyoNight"      => "Themes/TokyoNight.xaml",
                "Nord"            => "Themes/Nord.xaml",
                "Dracula"         => "Themes/Dracula.xaml",
                "MochaCatppuccin" => "Themes/CatppuccinMocha.xaml",
                "RosePine"        => "Themes/RosePine.xaml",
                "AyuDark"         => "Themes/AyuDark.xaml",
                "AyuMirage"       => "Themes/AyuMirage.xaml",
                "AyuLight"        => "Themes/AyuLight.xaml",
                "Cobalt2"         => "Themes/Cobalt2.xaml",
                "Monokai"         => "Themes/Monokai.xaml",
                "SolarizedDark"   => "Themes/SolarizedDark.xaml",
                "SolarizedLight"  => "Themes/SolarizedLight.xaml",
                _                 => $"Themes/{key}.xaml",
            };

            var dict = Current?.Resources?.MergedDictionaries;
            if (dict == null) return;

            // Brief cross-fade on the foreground window during the swap.
            // True cross-fade across every DynamicResource would mean
            // animating every brush — prohibitive complexity. A 120 ms
            // window-level opacity dip masks the brush flip without
            // compromising ReduceMotion users (we still respect their
            // preference and skip the animation when set). Theme change
            // is rare enough that a slightly-slower-than-instant feels
            // intentional, not laggy.
            var win = Current?.MainWindow;
            bool reduceMotion = false;
            try
            {
                // Honour ReduceMotion from BOTH the OS (Settings →
                // Accessibility → Visual effects → Animation effects)
                // AND the user's per-app preference. Either off
                // disables the cross-fade. WCAG 2.3.3 Animation from
                // Interactions (AAA) requires that this category of
                // motion respect a user opt-out.
                if (win is MainWindow mw && mw.IsLoaded)
                {
                    reduceMotion = SystemParameters.ClientAreaAnimation == false
                                || mw.Settings?.ReduceMotion == true;
                }
                else
                {
                    reduceMotion = SystemParameters.ClientAreaAnimation == false;
                }
            }
            catch { }

            void DoSwap()
            {
                // Theme palette lives at index 0 of MergedDictionaries
                // (App.xaml ordering: ThemeDict, _Indicators, _Buttons,
                // _Inputs). Replace index 0 only — Clear() would wipe
                // the style modules and every implicit Style would lose
                // its template, immediately white-rendering the entire
                // UI until the next ResourceDictionary load completed.
                var newTheme = new ResourceDictionary
                {
                    Source = new Uri(uri, UriKind.Relative)
                };
                if (dict.Count > 0)
                    dict[0] = newTheme;
                else
                    dict.Insert(0, newTheme);
                ApplyOverlayPalette();   // MatchTheme / HC read the new theme's brushes
            }

            if (win == null || !win.IsLoaded || reduceMotion)
            {
                DoSwap();
                return;
            }

            // Fade out → swap → fade in. 60 ms each side keeps total
            // change under 150 ms so it doesn't feel sluggish on slow
            // machines, while still being slow enough to perceive as
            // "intentional" rather than jarring.
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                To       = 0.55,
                Duration = TimeSpan.FromMilliseconds(60),
            };
            var fadeIn  = new System.Windows.Media.Animation.DoubleAnimation
            {
                To       = 1.0,
                Duration = TimeSpan.FromMilliseconds(60),
            };
            fadeOut.Completed += (s, e) =>
            {
                try { DoSwap(); } catch (Exception ex) { CrashLog.Write("ApplyTheme.Swap", ex); }
                try { win.BeginAnimation(UIElement.OpacityProperty, fadeIn); }
                catch (Exception ex) { CrashLog.Write("ApplyTheme.FadeIn", ex); }
            };
            try { win.BeginAnimation(UIElement.OpacityProperty, fadeOut); }
            catch (Exception ex)
            {
                CrashLog.Write("ApplyTheme.FadeOut", ex);
                DoSwap();   // animation failed — still swap so theme actually changes
            }
        }

        // ── Low-Vision: app-wide font family + badge font size ───────────────
        // The two helpers below mutate the App.xaml ResourceDictionary keys
        // ("AppFontFamily", "BadgeFontBase") at runtime. Any TextBlock /
        // control that binds via DynamicResource picks up the new value
        // without rebuilding the visual tree. Called from MainWindow on
        // startup (initial value) and from the settings UI whenever the
        // user changes the picker.

        /// <summary>
        /// Swap the app-wide font family at runtime. Low-vision users can pick
        /// OpenDyslexic, Atkinson Hyperlegible, Verdana, etc. Falls back to
        /// Segoe UI on null/empty input or invalid family names.
        /// </summary>
        public static void SetAppFontFamily(string family)
        {
            try
            {
                Current.Resources["AppFontFamily"] =
                    new System.Windows.Media.FontFamily(
                        string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family);
            }
            catch
            {
                Current.Resources["AppFontFamily"] =
                    new System.Windows.Media.FontFamily("Segoe UI");
            }
        }

        /// <summary>
        /// Set the badge text base size (px). Clamped to 8..72 so the resource
        /// is always a renderable, non-hostile value even if a caller passes
        /// something pathological.
        /// </summary>
        public static void SetBadgeFontSize(double size)
        {
            Current.Resources["BadgeFontBase"] = Math.Clamp(size, 8, 72);
        }

        // ── Application lifetime ──────────────────────────────────────────────
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Tune GC for an interactive, long-running UI process. The
            // default workstation GC's blocking Gen 2 collections can
            // produce visible mouse-pointer hitches at 30 Hz overlay
            // tick + 6.6 Hz UIA worker + InfoOverlay updates, especially
            // after several minutes when COM RCWs from
            // AutomationElement.FromPoint reads accumulate enough to
            // trigger a full Gen 2 cycle.
            //
            // SustainedLowLatency tells the GC to prefer background
            // collections and avoid full blocking compactions while
            // we're in this mode. We don't disable Gen 2 entirely
            // (that would leak fast), just bias toward background.
            //
            // Wrapped in try/catch because GCSettings throws on certain
            // hosts (CoreCLR readiness varies) and a GC tuning failure
            // shouldn't prevent the app from launching.
            try
            {
                System.Runtime.GCSettings.LatencyMode =
                    System.Runtime.GCLatencyMode.SustainedLowLatency;
            }
            catch (Exception ex) { CrashLog.Write("GC LatencyMode", ex); }

            // Restarted by App.Restart(): wait for the old process to finish exiting
            // (it may still be saving / tearing down hooks) before the duplicate check.
            WaitForRestartedParent(e.Args);

            bool createdNew;
            _instanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                _instanceMutex.Dispose();
                _instanceMutex = null;
                // Second launch → bring the running copy's window forward (suite pattern, S-10).
                if (!SignalExistingInstance())
                {
                    MessageBox.Show(
                        "Badge Overlay is already running.\n\nCheck the system tray.",
                        "Already Running",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                Shutdown(0);
                return;
            }
            StartActivationListener();

            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, ea) =>
                LogUnhandled("AppDomain.UnhandledException", ea.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ea) =>
            {
                LogUnhandled("UnobservedTaskException", ea.Exception);
                ea.SetObserved();
            };

            // Session disconnect/reconnect (RDP, Fast User Switching, Remote
            // Assistance) can hold our named mutex in an unexpected state.
            // When the session becomes active again, release and re-acquire
            // to avoid a ghost owner blocking legitimate re-launches.
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

            // Initialise High-Contrast monitor. Sync() is called later by
            // MainWindow once user theme preference is known.
            _hcMonitor = new HighContrastMonitor();

            // Heal a stale Run-key path if the user moved / reinstalled the
            // app. No-op when Start-with-Windows isn't enabled.
            try { StartupSettingsWindow.RefreshStartupPathIfStale(); }
            catch (Exception ex) { CrashLog.Write("RefreshStartupPathIfStale (OnStartup)", ex); }
        }

        private static void OnSessionSwitch(object sender,
            Microsoft.Win32.SessionSwitchEventArgs e)
        {
            try
            {
                switch (e.Reason)
                {
                    case Microsoft.Win32.SessionSwitchReason.SessionLock:
                    case Microsoft.Win32.SessionSwitchReason.SessionLogoff:
                    case Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect:
                    case Microsoft.Win32.SessionSwitchReason.RemoteDisconnect:
                        // Release the mutex so that during the disconnect
                        // window another process isn't blocked by a stale
                        // owner record if this process is about to be
                        // suspended or killed by the OS.
                        if (_instanceMutex != null)
                        {
                            try { _instanceMutex.ReleaseMutex(); } catch { /* not owned */ }
                        }
                        break;

                    case Microsoft.Win32.SessionSwitchReason.SessionUnlock:
                    case Microsoft.Win32.SessionSwitchReason.SessionLogon:
                    case Microsoft.Win32.SessionSwitchReason.ConsoleConnect:
                    case Microsoft.Win32.SessionSwitchReason.RemoteConnect:
                        // Re-acquire. If another instance grabbed it during
                        // the disconnect, this WaitOne returns false and we
                        // log, but keep running — exclusivity was already
                        // enforced at startup.
                        if (_instanceMutex != null)
                        {
                            try { _instanceMutex.WaitOne(0); } catch { }
                        }
                        break;
                }
            }
            catch (Exception ex) { CrashLog.Write("OnSessionSwitch", ex); }
        }

        private void OnUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
        {
            // Keep the overlay alive (decision 2026-10-12, BO-12) but do NOT save settings
            // here: in-memory settings may be half-applied after an unknown exception.
            // Settings already save on every change and on exit.
            LogUnhandled("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        }

        // ── Restart + second-launch activation ────────────────────────────────
        private static CancellationTokenSource _pipeCts;
        private static string ActivatePipeName =>
            "BadgeOverlay_Activate_" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

        /// <summary>
        /// Relaunch Badge Overlay. Releases the single-instance mutex first and passes
        /// <c>--restart &lt;pid&gt;</c> so the new process waits for this one to exit
        /// (no UI-thread sleep, no race with our own shutdown).
        /// </summary>
        /// <summary>True after Restart(discardSettings: true) — MainWindow skips saving settings.json.</summary>
        public static bool SuppressSettingsSave { get; private set; }

        public static void Restart(bool discardSettings = false)
        {
            // Reset-all moved settings.json aside; without this the window-closing save during
            // Shutdown would write the old in-memory settings straight back.
            SuppressSettingsSave = discardSettings;
            string exe = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            _pipeCts?.Cancel();
            ReleaseInstanceMutex();
            if (!string.IsNullOrEmpty(exe))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
                psi.ArgumentList.Add("--restart");
                psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                System.Diagnostics.Process.Start(psi);
            }
            Current.Shutdown(0);
        }

        /// <summary>Parses <c>--restart &lt;pid&gt;</c>; returns the pid or 0.</summary>
        internal static int ParseRestartPid(string[] args)
        {
            if (args == null) return 0;
            int i = Array.FindIndex(args, a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return 0;
            return i + 1 < args.Length
                && int.TryParse(args[i + 1], System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out int pid) && pid > 0
                ? pid : -1;   // -1 = restart flag without pid
        }

        private static void WaitForRestartedParent(string[] args)
        {
            int pid = ParseRestartPid(args);
            if (pid == 0) return;
            try
            {
                if (pid > 0)
                {
                    using var p = System.Diagnostics.Process.GetProcessById(pid);
                    p.WaitForExit(5000);
                    return;
                }
            }
            catch (ArgumentException) { return; }          // already gone
            catch (Exception ex) { CrashLog.Write("WaitForRestartedParent", ex); }

            // No pid: poll until the old instance's mutex disappears (≤ 5 s).
            var until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && Mutex.TryOpenExisting(MutexName, out var m))
            {
                m.Dispose();
                Thread.Sleep(100);
            }
        }

        private static bool SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", ActivatePipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(1500);
                using var w = new StreamWriter(client);
                w.WriteLine("activate");
                return true;
            }
            catch (Exception ex) { CrashLog.Write("SignalExistingInstance", ex); return false; }
        }

        private void StartActivationListener()
        {
            _pipeCts = new CancellationTokenSource();
            var token = _pipeCts.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(ActivatePipeName, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                        using var r = new StreamReader(server);
                        string msg = await r.ReadLineAsync(token).ConfigureAwait(false);
                        if (msg == "activate")
                            _ = Dispatcher.InvokeAsync(() => (MainWindow as BadgeOverlay.MainWindow)?.ActivateFromExternal());
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        CrashLog.Write("Activation listener", ex);
                        try { await Task.Delay(1000, token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    }
                }
            }, token);
        }

        /// <summary>
        /// Single funnel for the three top-level exception sinks
        /// (Dispatcher, AppDomain, TaskScheduler). CrashLog.Write already
        /// swallows its own failures internally, so no outer try/catch is
        /// needed here — the previous defensive wrapping was dead defence.
        /// </summary>
        private static void LogUnhandled(string source, Exception ex)
        {
            if (ex == null) return;
            CrashLog.Write(source, ex);
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);
            try
            {
                MainWindow mw = MainWindow as MainWindow;
                if (mw != null) mw.TrySaveSettings();
            }
            catch (Exception ex) { CrashLog.Write("OnSessionEnding", ex); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            try { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
            try
            {
                _hcMonitor?.Dispose();
                _hcMonitor = null;
            }
            catch { }

            // Safety-net: any subsystem that forgot to dispose its
            // WinEventHookManager subscriptions gets cleaned up here.
            // Subsystems still dispose their own subs in their own
            // teardown paths so hooks come down at the same time as
            // the windows that depend on them; this is the last line
            // of defence.
            try { WinEventHookManager.DisposeAll(); }
            catch (Exception ex) { CrashLog.Write("WinEventHookManager.DisposeAll", ex); }

            try { _pipeCts?.Cancel(); } catch { }
            try
            {
                ReleaseInstanceMutex();
            }
            catch { }
        }

        /// <summary>
        /// Releases and disposes the single-instance mutex. Idempotent;
        /// safe to call multiple times (subsequent calls are no-ops).
        /// Public so the About → Reset path can release the mutex
        /// before spawning a replacement process — without this, the
        /// replacement's "already running" check would fail because
        /// we still hold the mutex during shutdown.
        /// </summary>
        public static void ReleaseInstanceMutex()
        {
            try
            {
                if (_instanceMutex != null)
                {
                    try { _instanceMutex.ReleaseMutex(); } catch { /* not owned */ }
                    _instanceMutex.Dispose();
                    _instanceMutex = null;
                }
            }
            catch { /* swallow: shutting down */ }
        }
    }
}
