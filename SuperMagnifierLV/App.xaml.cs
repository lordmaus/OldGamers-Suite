using System;
using System.Threading;
using System.Windows;

namespace SuperMagnifier
{
    public partial class App : Application
    {
        // ── Theme state ───────────────────────────────────────────────────────
        public static string CurrentTheme { get; private set; } = "Dark";
        public static bool   IsDark       => !s_lightThemes.Contains(CurrentTheme);

        private static readonly System.Collections.Generic.HashSet<string> s_lightThemes
            = new(System.StringComparer.OrdinalIgnoreCase)
            { "Light", "WarmLight", "CatppuccinLatte", "GitHubLight", "AyuLight", "SolarizedLight", "RosePineDawn", "EverforestLight", "PaperWhite", "Sepia", "Arctic", "Ivory", "HighContrastLight" };

        public static bool IsLightTheme(string key)
            => s_lightThemes.Contains(key ?? "");

        private static HighContrastMonitor _hcMonitor;
        public  static bool IsHighContrast => _hcMonitor?.IsHighContrast ?? false;

        private System.ComponentModel.PropertyChangedEventHandler _reduceMotionHandler;

        /// <summary>
        /// True when the OS "Animate controls and elements" preference is off.
        /// Gate any Storyboard / transition on this flag — WCAG 3.0 draft prep.
        /// </summary>
        public static bool ReduceMotion { get; private set; }
            = !System.Windows.SystemParameters.ClientAreaAnimation;

        // ── Single-instance mutex ─────────────────────────────────────────────
        private static Mutex _instanceMutex;
        private const  string MutexName = "Global\\SuperMagnifierLV_SingleInstance";

        // ── Theme API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Swap the active theme by name. Respects High Contrast — if HC is active
        /// the monitor retains the preference but keeps the HighContrast theme loaded.
        /// Valid keys: Dark, OneDark, GruvboxDark, TokyoNight, Nord, Dracula, Mocha,
        /// RosePine, Light, WarmLight, CatppuccinLatte, GitHubLight.
        /// </summary>
        public static void SetTheme(string key)
        {
            CurrentTheme = key ?? "Dark";
            // Tell the monitor the user's intent so it restores correctly when HC ends
            _hcMonitor?.Sync(CurrentTheme);

            // If HC is currently active the monitor already applied HighContrast — done
            if (_hcMonitor != null && _hcMonitor.IsHighContrast) return;

            ApplyTheme(CurrentTheme);
        }

        /// <summary>Convenience overload: true → "Dark", false → "Light".</summary>
        public static void SetTheme(bool dark)
            => SetTheme(dark ? "Dark" : "Light");

        /// <summary>
        /// Low-level theme loader — swaps the merged resource dictionary.
        /// Called by <see cref="SetTheme(string)"/> and by <see cref="HighContrastMonitor"/>.
        /// </summary>
        /// <summary>Maps a theme key to its embedded-resource relative URI. A few keys are
        /// aliases whose file name doesn't match the key (e.g. "Mocha" -> CatppuccinMocha.xaml);
        /// everything else falls back to Themes/{key}.xaml. Shared with AppSettings.Load()'s
        /// ThemeName validation (S-1, SuperMagnifierLV_CodeSecA11y_Review_2026-09-14) so the two
        /// never drift apart.</summary>
        public static string ResolveThemeUri(string key) => key switch
        {
            "Light"           => "Themes/Light.xaml",
            "WarmLight"       => "Themes/WarmLight.xaml",
            "CatppuccinLatte" => "Themes/CatppuccinLatte.xaml",
            "GitHubLight"     => "Themes/GitHubLight.xaml",
            "HighContrast"    => "Themes/HighContrast.xaml",
            "OneDark"         => "Themes/OneDark.xaml",
            "GruvboxDark"     => "Themes/GruvboxDark.xaml",
            "TokyoNight"      => "Themes/TokyoNight.xaml",
            "Nord"            => "Themes/Nord.xaml",
            "Dracula"         => "Themes/Dracula.xaml",
            "Mocha"           => "Themes/CatppuccinMocha.xaml",
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

        /// <summary>True if key resolves to a theme resource that's actually embedded in the
        /// assembly. Used by AppSettings.Load() to normalize a hand-edited/corrupted ThemeName
        /// once at the trust boundary instead of every downstream consumer guarding against it.</summary>
        public static bool IsKnownThemeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            try
            {
                var uri = new Uri(ResolveThemeUri(key), UriKind.Relative);
                return GetResourceStream(uri) != null;
            }
            catch
            {
                return false;
            }
        }

        private static ResourceDictionary s_colorIcons;
        /// <summary>Theming/ColorIcons.xaml, parsed once and re-merged by ApplyTheme after each Clear().</summary>
        private static ResourceDictionary ColorIcons => s_colorIcons ??= new ResourceDictionary
        {
            Source = new Uri("Theming/ColorIcons.xaml", UriKind.Relative)
        };

        public static void ApplyTheme(string key)
        {
            string uri = ResolveThemeUri(key);

            // Bug fix: a hand-edited/corrupted ThemeName (e.g. a typo'd or removed
            // theme key) used to build a Uri that failed to resolve, throwing during
            // window construction before any UI is visible -- a crash-on-launch for a
            // low-vision accessibility tool, the worst possible failure mode for this
            // app's audience. Build and validate the new dictionary BEFORE touching
            // the live MergedDictionaries, so a bad key leaves the current theme intact
            // instead of leaving the app with no theme at all; fall back hard to Dark.
            try
            {
                var newDict = new ResourceDictionary
                {
                    Source = new Uri(uri, UriKind.Relative)
                };
                var dict = Current.Resources.MergedDictionaries;
                dict.Clear();
                dict.Add(newDict);
                // Fixed-color JT icons are theme-independent but live in the same MergedDictionaries,
                // so Clear() above removes them on every theme change — re-add right after the theme.
                dict.Add(ColorIcons);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SuperMagnifier] ApplyTheme('{key}') failed, falling back to Dark: {ex.Message}");
                if (!string.Equals(key, "Dark", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentTheme = "Dark";
                    ApplyTheme("Dark");
                }
            }
        }

        // ── App-wide font family + explicit HUD font size ────────────────────
        // Low-Vision users get a real font picker (Segoe UI default) and an
        // explicit HUD size slider (8..72 px) on top of the existing UiScale
        // transform. Resource keys consumed by MainWindow.xaml + dialog styles.
        public static void SetAppFontFamily(string family)
        {
            try { Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family); }
            catch { Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily("Segoe UI"); }
        }

        public static void SetHudFontSize(double size)
        {
            Current.Resources["HudFontBase"] = Math.Clamp(size, 8, 72);
        }

        // ── Startup ───────────────────────────────────────────────────────────

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Single-instance guard ────────────────────────────────────────
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName,
                                       out bool createdNew);
            if (!createdNew)
            {
                // S-10 (2026-10-13): bring the running instance forward instead of "Already running";
                // the box is only the fallback when the running instance can't be reached.
                if (!InstanceActivation.SignalExisting("SuperMagnifierLV"))
                    MessageBox.Show(
                        "Super Magnifier LV is already running.\n\nCheck the system tray.",
                        "Already Running",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                _instanceMutex.Dispose();
                Shutdown(0);
                return;
            }
            InstanceActivation.Listen("SuperMagnifierLV", () => InstanceActivation.BringToFront(Current.MainWindow));

            // ── High Contrast monitor ────────────────────────────────────────
            // Must be created before MainWindow loads so the first theme swap
            // goes through ApplyTheme and not the old SetTheme(dark) path.
            _hcMonitor = new HighContrastMonitor();

            // WCAG 3.0 draft — reduce-motion: update flag when OS setting changes at runtime
            _reduceMotionHandler = (_, ev) =>
            {
                if (ev.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                    ReduceMotion = !SystemParameters.ClientAreaAnimation;
            };
            SystemParameters.StaticPropertyChanged += _reduceMotionHandler;

            // ── Crash handlers — settings save + engine restore ───────────────
            // DispatcherUnhandledException: unhandled exceptions on the UI thread
            DispatcherUnhandledException += (s, args) =>
            {
                CrashLog.Write("DispatcherUnhandledException", args.Exception);   // SM-05: record first
                TryDisposeEngineOnCrash();
                SaveSettingsOnCrash();
                // Do NOT mark handled — let WPF show the default crash dialog
            };

            // AppDomain.UnhandledException: unhandled exceptions on background threads.
            // MainWindow is a DispatcherObject — the save must be marshalled to the UI thread
            // (it previously threw VerifyAccess here and was swallowed, so nothing was saved).
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                CrashLog.Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);
                TryDisposeEngineOnCrash();
                SaveSettingsOnCrash();
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                CrashLog.Write("UnobservedTaskException", args.Exception);
                args.SetObserved();
            };
        }

        // ── Session ending (logoff / shutdown) ────────────────────────────────
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);
            try
            {
                if (MainWindow is MainWindow mw)
                    mw.TrySaveSettingsOnCrash();
            }
            catch { }
        }

        // ── Exit ──────────────────────────────────────────────────────────────
        protected override void OnExit(ExitEventArgs e)
        {
            InstanceActivation.Stop();
            base.OnExit(e);
            if (_reduceMotionHandler != null)
            {
                SystemParameters.StaticPropertyChanged -= _reduceMotionHandler;
                _reduceMotionHandler = null;
            }
            _hcMonitor?.Dispose();
            _hcMonitor = null;
            try { _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Calls MagnifierEngine.Dispose() via the MainWindow on crash to restore
        /// any system-level state (SPI_SETFONTSMOOTHING) before the process exits.
        /// </summary>
        private void SaveSettingsOnCrash()
        {
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    (MainWindow as MainWindow)?.TrySaveSettingsOnCrash();
                }
                else
                {
                    var op = Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.TrySaveSettingsOnCrash()));
                    op.Wait(TimeSpan.FromSeconds(2));   // bounded: the UI thread may itself be wedged
                }
            }
            catch (Exception ex) { CrashLog.Write("SaveSettingsOnCrash", ex); }
        }

        private static void TryDisposeEngineOnCrash()
        {
            try
            {
                if (Current?.MainWindow is MainWindow mw)
                    mw.TryDisposeEngine();
            }
            catch { }
        }
    }
}
