using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ReticleOverlay
{
    public partial class App : Application
    {
        // ── Theme registry ────────────────────────────────────────────────────
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

        private static readonly HashSet<string> _lightThemes = new HashSet<string>
            { "Light", "WarmLight", "CatppuccinLatte", "GitHubLight", "AyuLight", "SolarizedLight", "RosePineDawn", "EverforestLight", "PaperWhite", "Sepia", "Arctic", "Ivory", "HighContrastLight" };

        private static string _themeName = "Dark";
        public  static string ThemeName  => _themeName;
        public  static bool   IsDark     => !_lightThemes.Contains(_themeName);

        /// <summary>
        /// True when the OS "Animate controls and elements" preference is off.
        /// Gate any Storyboard / transition on this flag — WCAG 3.0 draft prep.
        /// </summary>
        public static bool ReduceMotion { get; private set; }
            = !System.Windows.SystemParameters.ClientAreaAnimation;

        // ── Apply ─────────────────────────────────────────────────────────────
        private static ResourceDictionary s_colorIcons;
        /// <summary>Theming/ColorIcons.xaml, parsed once and re-merged after every MergedDictionaries.Clear()
        /// (SetTheme and ApplyHighContrast) — the fixed-color icons are theme-independent.</summary>
        private static ResourceDictionary ColorIcons => s_colorIcons ??= new ResourceDictionary
        {
            Source = new Uri("Theming/ColorIcons.xaml", UriKind.Relative)
        };

        public static void SetTheme(string name)
        {
            if (string.IsNullOrEmpty(name) || Array.IndexOf(ThemeNames, name) < 0)
                name = "Dark";
            _themeName                   = name;
            AppPrefs.Instance.ThemeName  = name;
            AppPrefs.Instance.DarkTheme  = IsDark;   // keep backward-compat field in sync
            var dict = Current.Resources.MergedDictionaries;
            dict.Clear();
            dict.Add(new ResourceDictionary
            {
                Source = new Uri($"Themes/{name}.xaml", UriKind.Relative)
            });
            dict.Add(ColorIcons);
        }

        public static void CycleTheme()
        {
            int idx = Array.IndexOf(ThemeNames, _themeName);
            idx = (idx + 1) % ThemeNames.Length;
            SetTheme(ThemeNames[idx]);
        }

        // ── Low-vision: app-wide font family + HUD font size ──────────────────
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
        private static Mutex _instanceMutex;
        // RO-09: Local\ (per Windows session) — Global\ blocked a second signed-in user entirely.
        private const  string MutexName = "Local\\ReticleOverlay_SingleInstance";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Bug #11 (2026-09-07 review): no unhandled-exception handler meant any
            // exception on the UI thread hard-crashed the process with no logging and
            // no chance to flush pending prefs/profile changes. This does not swallow
            // the crash (DispatcherUnhandledException.Handled is left false) -- it only
            // logs for diagnosability and best-effort flushes AppPrefs before the app dies.
            DispatcherUnhandledException += (s, ev) => LogUnhandledException("UI", ev.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                LogUnhandledException("AppDomain", ev.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                LogUnhandledException("Task", ev.Exception);
                ev.SetObserved();
            };

            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // S-10 (2026-10-13): bring the running instance forward instead of "Already running";
                // the box is only the fallback when the running instance can't be reached.
                if (!InstanceActivation.SignalExisting("ReticleOverlay"))
                    MessageBox.Show(
                        "Reticle Overlay is already running.\n\nCheck the system tray.",
                        "Already Running",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                _instanceMutex.Dispose();
                Shutdown(0);
                return;
            }
            InstanceActivation.Listen("ReticleOverlay", () => InstanceActivation.BringToFront(Current.MainWindow));

            AppPrefs.Instance.Load();

            // Apply low-vision font preferences before any window opens
            SetAppFontFamily(AppPrefs.Instance.AppFontFamily);
            SetHudFontSize(AppPrefs.Instance.HudFontSize);

            // Migration: ThemeName="" in old prefs → derive from legacy DarkTheme bool
            string theme = AppPrefs.Instance.ThemeName;
            if (string.IsNullOrEmpty(theme))
                theme = AppPrefs.Instance.DarkTheme ? "Dark" : "Light";
            SetTheme(theme);

            if (SystemParameters.HighContrast)
                ApplyHighContrast();

            // Re-apply if user toggles HC while app is running
            SystemParameters.StaticPropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(SystemParameters.HighContrast))
                    Current.Dispatcher.Invoke(() =>
                    {
                        if (SystemParameters.HighContrast) ApplyHighContrast();
                        else SetTheme(AppPrefs.Instance.ThemeName);
                    });
                // WCAG 3.0 draft — reduce-motion: update flag when OS setting changes at runtime
                if (ev.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                    ReduceMotion = !SystemParameters.ClientAreaAnimation;
            };
        }

        private static void LogUnhandledException(string source, Exception ex)
        {
            try
            {
                CrashLog.Write("Unhandled (" + source + ")", ex);   // RO-03: Release builds now keep a trace
                // Prefs are UI-owned — save on the UI thread (2 s cap when called from a worker).
                var d = Current?.Dispatcher;
                if (d == null || d.CheckAccess()) AppPrefs.Instance.Save();
                else d.Invoke(AppPrefs.Instance.Save, System.Windows.Threading.DispatcherPriority.Send,
                              CancellationToken.None, TimeSpan.FromSeconds(2));
            }
            catch { /* best-effort -- never let logging itself crash the crash handler */ }
        }

        private static void ApplyHighContrast()
        {
            var dict = Current.Resources.MergedDictionaries;
            dict.Clear();
            dict.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/HighContrast.xaml", UriKind.Relative)
            });
            dict.Add(ColorIcons);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            InstanceActivation.Stop();
            base.OnExit(e);
            try { _instanceMutex?.ReleaseMutex(); _instanceMutex?.Dispose(); }
            catch { }
        }
    }
}
