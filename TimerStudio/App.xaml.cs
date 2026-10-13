using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace TimerStudio
{
    public partial class App : Application
    {
        private static bool  _isDark = true;
        public  static bool  IsDark  => _isDark;

        /// <summary>
        /// True when the OS "Animate controls and elements" preference is off.
        /// Gate any Storyboard / transition on this flag — WCAG 3.0 draft prep.
        /// </summary>
        public static bool ReduceMotion { get; private set; }
            = !System.Windows.SystemParameters.ClientAreaAnimation;

        private static Mutex _instanceMutex;
        private const  string MutexName = "Local\\TimerStudio_SingleInstance";
        public  const  string RestartArg = "--restart";

        /// <summary>
        /// TS-08: restart this app. Saves timers, launches a new instance with
        /// <see cref="RestartArg"/> (it waits on the single-instance mutex), then shuts down.
        /// </summary>
        public static void Restart()
        {
            if (Current.MainWindow is TimerStudio.MainWindow mw)
            {
                try { mw.PrepareForRestart(); }
                catch (Exception ex) { DiagLog.WriteException("App.Restart save", ex); }
            }
            string exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                // ShellExecute so the new process starts at the shell's integrity level.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = exePath,
                    Arguments       = RestartArg,
                    UseShellExecute = true
                });
            }
            Current.Shutdown();
        }

        // ── Theme names ───────────────────────────────────────────────────────
        public static readonly string[] ThemeNames =
        {
            // Dark
            "Dark", "OneDark", "GruvboxDark", "TokyoNight", "Nord", "Dracula", "CatppuccinMocha", "RosePine", 
            "AyuDark", "AyuMirage", "Cobalt2", "Monokai", "SolarizedDark", "Kanagawa", "EverforestDark", 
            "Oxocarbon", "Palenight", "GitHubDark", "DraculaPro", "Horizon", "Abyss", "Obsidian", "Panda", 
            "Monochrome",
            // Light
            "Light", "WarmLight", "CatppuccinLatte", "GitHubLight", "AyuLight", "SolarizedLight", 
            "RosePineDawn", "EverforestLight", "PaperWhite", "Sepia", "Arctic", "Ivory",
            // Contrast
            "HighContrast", "HighContrastLight", "HCBlue", "HCGreen", "HCAmber",
            // Neon
            "Synthwave", "Cyberpunk", "NeonCity", "VaporWave", "Outrun", "HoloBlue", "Matrix", "NeonPurple", 
            "LaserGrid", "ElectricOcean",
            // Nature
            "Forest", "Ocean", "DesertSand", "Autumn", "MidnightGarden", "LavenderField", "CoralReef", 
            "Tundra", "Volcanic", "Meadow",
            // Retro
            "C64", "AmberCRT", "GreenCRT", "IBMBlue", "AppleII", "BIOS", "Teletext", "TerminalRed", 
            "VGAPurple", "ZXSpectrum",
            // Warm
            "Ember", "Rosewood", "GoldenHour", "Terracotta", "Rust", "Mahogany", "Cinnamon", "Wine", 
            "Copper", "Sundowner",
            // Cool
            "MidnightBlue", "DeepSpace", "Glacier", "Cobalt", "Sapphire", "Periwinkle", "ArcticFox", "Steel", 
            "Slate", "IndigoNight",
            // Pastel
            "CottonCandy", "MintCream", "BabyBlue", "Peach", "Lilac", "Buttercup", "Blush", "Seafoam", 
            "Wisteria", "Dusk",
            // Specialty
            "CoffeeShop", "Denim", "Malachite", "SolarFlare", "Moonstone", "Bubblegum",
        };

                private static readonly System.Collections.Generic.HashSet<string> _lightThemes =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "Light", "WarmLight", "CatppuccinLatte", "GitHubLight", "AyuLight", "SolarizedLight", "RosePineDawn", "EverforestLight", "PaperWhite", "Sepia", "Arctic", "Ivory", "HighContrastLight"
            };

        /// <summary>
        /// Applies a named theme and persists the choice.
        /// <paramref name="name"/> must match one of <see cref="ThemeNames"/>.
        /// </summary>
        public static void SetThemeByName(string name, bool saveToPrefs = true)
        {
            if (string.IsNullOrEmpty(name)) name = "Dark";
            _isDark = !_lightThemes.Contains(name);
            if (saveToPrefs)
            {
                AppPrefs.Instance.DarkTheme     = _isDark;
                AppPrefs.Instance.SelectedTheme = name;
                AppPrefs.Instance.Save();
            }
            ApplyThemeUri("Themes/" + name + ".xaml");
        }

        /// <summary>Backward-compat helper — maps bool to "Dark"/"Light".</summary>
        public static void SetTheme(bool dark, bool saveToPrefs = true)
            => SetThemeByName(dark ? "Dark" : "Light", saveToPrefs);

        // ── Feature 6: Windows dark/light mode detection ──────────────────────
        /// <summary>Returns true when Windows "Apps use dark mode" is active.</summary>
        public static bool IsWindowsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;   // 0 = dark; 1 = light
            }
            catch { return true; }  // default to dark on error
        }

        /// <summary>Apply Windows color scheme if ThemeAuto is enabled.</summary>
        public static void ApplyAutoThemeIfEnabled()
        {
            if (!AppPrefs.Instance.ThemeAuto) return;
            if (SystemParameters.HighContrast)  return;  // HC overrides auto
            bool dark = IsWindowsDarkMode();
            SetTheme(dark, saveToPrefs: false);  // don't persist — it's auto
        }

        /// <summary>Swaps the active theme-dict entry to the given relative URI.</summary>
        private static void ApplyThemeUri(string uri)
        {
            var target = new System.Uri(uri, System.UriKind.Relative);
            var dict   = Current.Resources.MergedDictionaries;
            for (int i = 0; i < dict.Count; i++)
            {
                var src = dict[i].Source?.OriginalString ?? "";
                if (src.StartsWith("Themes/", System.StringComparison.OrdinalIgnoreCase))
                {
                    dict[i] = new ResourceDictionary { Source = target };
                    return;
                }
            }
            dict.Add(new ResourceDictionary { Source = target });
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Unhandled-exception logging (TS-07) ───────────────────────────
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    DiagLog.WriteException("AppDomain.UnhandledException (terminating=" + args.IsTerminating + ")",
                        ex, redactEmails: true);
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                DiagLog.WriteException("TaskScheduler.UnobservedTaskException", args.Exception, redactEmails: true);
                args.SetObserved();
            };

            // ── Single-instance guard ─────────────────────────────────────────
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            // TS-08: a --restart launch waits for the previous instance to exit
            // instead of reporting "already running" and quitting.
            if (!createdNew && Array.IndexOf(e.Args, RestartArg) >= 0)
            {
                try { createdNew = _instanceMutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { createdNew = true; }
            }
            if (!createdNew)
            {
                MessageBox.Show(
                    "Timer Studio is already running.\n\nCheck the system tray.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _instanceMutex.Dispose();
                Shutdown(0);
                return;
            }

            // ── Load persisted prefs (window size, theme) ─────────────────────
            AppPrefs.Instance.Load();

            // Determine which theme to apply at startup
            string startTheme = AppPrefs.Instance.SelectedTheme ?? "Dark";

            // Auto-theme overrides the stored theme name with Dark or Light
            if (AppPrefs.Instance.ThemeAuto && !SystemParameters.HighContrast)
                startTheme = IsWindowsDarkMode() ? "Dark" : "Light";

            _isDark = !_lightThemes.Contains(startTheme);

            // Apply theme immediately before any window appears
            var themeDict = Current.Resources.MergedDictionaries;
            themeDict.Clear();
            themeDict.Add(new ResourceDictionary
            {
                Source = new System.Uri("Themes/" + startTheme + ".xaml",
                                        System.UriKind.Relative)
            });
            // Fixed-color JT icons (theme-independent). Clear() above removed the App.xaml copy;
            // ApplyThemeUri only swaps the "Themes/" entry, so this survives every later theme change.
            themeDict.Add(new ResourceDictionary
            {
                Source = new System.Uri("Theming/ColorIcons.xaml", System.UriKind.Relative)
            });

            // ── High Contrast detection ───────────────────────────────────────
            // If Windows High Contrast is active load HighContrast.xaml (SystemColors-based).
            // Also subscribe to runtime HC changes so the theme updates without a restart.
            if (SystemParameters.HighContrast)
                ApplyThemeUri("Themes/HighContrast.xaml");

            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast))
                {
                    if (SystemParameters.HighContrast)
                        ApplyThemeUri("Themes/HighContrast.xaml");
                    else
                        ApplyThemeUri("Themes/" + (AppPrefs.Instance.SelectedTheme ?? "Dark") + ".xaml");
                }
                // WCAG 3.0 draft — reduce-motion: update flag when OS setting changes at runtime
                if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                    ReduceMotion = !SystemParameters.ClientAreaAnimation;
            };

            // ── Auto-theme: follow Windows colour scheme at runtime ───────────────
            // Named handler so it can be unsubscribed in OnExit — prevents the lambda
            // from pinning the App instance as a static GC root for process lifetime.
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // ── JSON persistence self-test ────────────────────────────────────
            if (!TimerPersistence.SelfTest())
            {
                System.Diagnostics.Debug.WriteLine("[TimerStudio] WARNING: TimerPersistence self-test failed — serialisation may be broken");
            }

            // ── Save timers on unhandled crash ────────────────────────────────
            DispatcherUnhandledException += (s, args) =>
            {
                DiagLog.WriteException("DispatcherUnhandledException", args.Exception, redactEmails: true);
                try
                {
                    if (MainWindow is MainWindow mw)
                        mw.TrySaveSettingsOnCrash();
                }
                catch { }
                // Don't mark handled — let WPF show the default crash dialog
            };
        }

        private void OnUserPreferenceChanged(object sender,
            Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Dispatcher.InvokeAsync(ApplyAutoThemeIfEnabled);
        }

        // ── Windows logoff / shutdown — save before process is killed ─────────
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

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnExit(e);
            try { _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); }
            catch { }
        }
    }
}
