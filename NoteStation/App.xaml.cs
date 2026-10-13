using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace NoteStation
{
    public partial class App : Application
    {
        // ── Theme registry ────────────────────────────────────────────────────
                // ── Theme names ───────────────────────────────────────────────────────
        public static readonly string[] ThemeNames = new[]
        {
            // Dark
            "Dark", "OneDark", "GruvboxDark", "TokyoNight", "Nord", "Dracula", "CatppuccinMocha", "RosePine", 
            "AyuDark", "AyuMirage", "Cobalt2", "Monokai", "SolarizedDark", "Kanagawa", "EverforestDark", 
            "Oxocarbon", "Palenight", "GitHubDark", "DraculaPro", "Horizon", "Abyss", "Obsidian", "Panda", 
            "Monochrome", "Editorial",
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

        private static readonly HashSet<string> _lightThemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Light", "WarmLight", "CatppuccinLatte", "GitHubLight", "AyuLight", "SolarizedLight", "RosePineDawn", "EverforestLight", "PaperWhite", "Sepia", "Arctic", "Ivory", "HighContrastLight"
        };

        // Current theme name
        public static string CurrentTheme { get; private set; } = "Dark";
        public static bool IsDark => !_lightThemes.Contains(CurrentTheme);

        // ── App version (read from assembly) ──────────────────────────────────
        public static string Version { get; } = VersionInfo.Display;

        // ── App data directory ────────────────────────────────────────────────
        public static readonly string AppDataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NoteStation");

        // ── Crash log path ────────────────────────────────────────────────────
        private static readonly string _crashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NoteStation", "crash.log");

        // Guard against nested error dialogs cascading into a stack overflow.
        private bool _showingErrorDialog;

        /// <summary>True once Restart()/fatal shutdown started — MainWindow skips close-to-tray logic.</summary>
        public static bool ShutdownRequested { get; private set; }

        // ── Single instance ───────────────────────────────────────────────────
        private const string SingleInstanceMutexName = @"Local\NoteStation_SingleInstance";
        private static Mutex _singleInstance;
        private static bool _ownsSingleInstance;
        private static CancellationTokenSource _pipeCts;
        private static string ActivatePipeName =>
            "NoteStation_Activate_" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

        // ── App entry point ───────────────────────────────────────────────────
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Directory.CreateDirectory(AppDataDir);

            // Global exception handlers — log crashes instead of silent termination (WACK req)
            DispatcherUnhandledException += (_, ex) =>
            {
                // Always log every exception.
                LogCrash("UI thread", ex.Exception);
                ex.Handled = true;

                // Unknown faults may have left a DataStore mutation half-applied — stop writing
                // to disk and close instead of limping on and autosaving corrupted state.
                if (!IsRecoverable(ex.Exception))
                {
                    FatalShutdown(ex.Exception);
                    return;
                }

                // Re-entrancy guard: a layout/measure exception re-fires on every message
                // pump. MessageBox.Show pumps messages, so showing a dialog per exception
                // stacks nested dialogs until the stack overflows (cascade crash). Show at
                // most one dialog at a time; swallow (but still log) any that arrive while
                // a dialog is already up.
                if (_showingErrorDialog) return;
                _showingErrorDialog = true;
                try
                {
                    var root = ex.Exception;
                    while (root.InnerException != null) root = root.InnerException;
                    string where = root.TargetSite != null
                        ? $"{root.TargetSite.DeclaringType?.Name}.{root.TargetSite.Name}"
                        : "(unknown)";
                    MessageBox.Show(
                        $"An unexpected error occurred:\n\n" +
                        $"{root.GetType().Name}: {root.Message}\n\n" +
                        $"At: {where}\n\nDetails have been written to crash.log.",
                        "NoteStation — Unexpected Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { _showingErrorDialog = false; }
            };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                DataStore.Freeze();
                if (ex.ExceptionObject is Exception exception)
                    LogCrash("Background thread", exception);
            };
            TaskScheduler.UnobservedTaskException += (_, ex) =>
            {
                LogCrash("Unobserved task", ex.Exception);
                ex.SetObserved();
            };

            if (!AcquireSingleInstance(e.Args.Contains("--restart", StringComparer.OrdinalIgnoreCase)))
            {
                SignalExistingInstance();
                Shutdown(0);
                return;
            }

            // Load saved prefs (theme, etc.)
            AppPrefs.Instance.Load();

            // Apply saved theme
            SetTheme(AppPrefs.Instance.SelectedTheme);

            // Created here (not StartupUri) so a second instance never builds a window.
            var main = new NoteStation.MainWindow();
            MainWindow = main;
            main.Show();
            StartActivationListener();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _pipeCts?.Cancel();
            ReleaseSingleInstance();
            base.OnExit(e);
        }

        // ── Error policy ──────────────────────────────────────────────────────
        /// <summary>Known-transient UI faults that leave no half-written data behind.</summary>
        internal static bool IsRecoverable(Exception ex)
        {
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            if (root is System.Windows.Markup.XamlParseException) return true;   // rich-text load
            if (root is ExternalException) return true;                           // busy/locked clipboard (COMException derives)
            string ns = root.TargetSite?.DeclaringType?.Namespace ?? "";
            if (root is InvalidOperationException && ns.StartsWith("System.Windows", StringComparison.Ordinal))
                return true;                                                      // layout / measure / binding
            return false;
        }

        private void FatalShutdown(Exception ex)
        {
            DataStore.Freeze();
            if (ShutdownRequested) return;
            ShutdownRequested = true;
            if (!_showingErrorDialog)   // another error box already up → skip the dialog, still close
            {
                _showingErrorDialog = true;
                try { new FatalErrorDialog(ex, _crashLog).ShowDialog(); }
                catch (Exception dlgEx) { LogCrash("Fatal dialog", dlgEx); }
            }
            Shutdown(1);
        }

        // ── Single instance + restart ─────────────────────────────────────────
        private static bool AcquireSingleInstance(bool isRestart)
        {
            _singleInstance = new Mutex(false, SingleInstanceMutexName);
            try
            {
                // A restart is launched while the old process still holds the mutex — wait for it to exit.
                _ownsSingleInstance = _singleInstance.WaitOne(isRestart ? TimeSpan.FromSeconds(5) : TimeSpan.Zero);
            }
            catch (AbandonedMutexException) { _ownsSingleInstance = true; }
            return _ownsSingleInstance;
        }

        internal static void ReleaseSingleInstance()
        {
            if (!_ownsSingleInstance) return;
            try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { }
            _ownsSingleInstance = false;
        }

        /// <summary>Relaunch NoteStation (used after import/restore/clear). Releases the
        /// single-instance mutex first so the new process doesn't exit as a duplicate.</summary>
        public static void Restart()
        {
            ShutdownRequested = true;
            string exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            _pipeCts?.Cancel();
            ReleaseSingleInstance();
            if (!string.IsNullOrEmpty(exe))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
                psi.ArgumentList.Add("--restart");
                System.Diagnostics.Process.Start(psi);
            }
            Current.Shutdown();
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", ActivatePipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(1500);
                using var w = new StreamWriter(client);
                w.WriteLine("activate");
            }
            catch (Exception ex) { LogCrash("Activate existing instance", ex); }
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
                            _ = Dispatcher.InvokeAsync(() => (MainWindow as NoteStation.MainWindow)?.ActivateFromExternal());
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { LogCrash("Activation listener", ex); await Task.Delay(1000, token).ConfigureAwait(false); }
                }
            }, token);
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_crashLog)!);
                File.AppendAllText(_crashLog,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch { /* best-effort */ }
        }

        // ── Theme switching ───────────────────────────────────────────────────
        public static void SetTheme(string name)
        {
            // Validate
            bool found = false;
            foreach (var n in ThemeNames) { if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { found = true; break; } }
            if (!found) name = "Dark";

            CurrentTheme = name;

            var uri = new Uri($"Themes/{name}.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };

            // Replace first merged dictionary (the theme slot)
            var merged = Current.Resources.MergedDictionaries;
            if (merged.Count > 0) merged[0] = dict;
            else merged.Add(dict);
        }

        public static void CycleTheme()
        {
            int idx = Array.IndexOf(ThemeNames, CurrentTheme);
            idx = (idx + 1) % ThemeNames.Length;
            SetTheme(ThemeNames[idx]);
            AppPrefs.Instance.SelectedTheme = CurrentTheme;
            AppPrefs.Instance.DeferredSave();
        }

        // ── Content font sizes ────────────────────────────────────────────────
        public static void SetContentFontSizes(double clip, double note, double task, double folder)
        {
            Current.Resources["ClipFontBase"]   = clip;
            Current.Resources["NoteFontBase"]   = note;
            Current.Resources["TaskFontBase"]   = task;
            Current.Resources["FolderFontBase"] = folder;
        }

        public static void SetFolderAccentWidth(double width)
            => Current.Resources["FolderAccentBarWidth"] = width;

        /// <summary>
        /// Swap the app-wide font family at runtime. Low-vision users can pick
        /// OpenDyslexic, Atkinson Hyperlegible, Verdana, etc. Falls back to
        /// Segoe UI on null/empty input or invalid family names.
        /// </summary>
        public static void SetAppFontFamily(string familyName)
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(familyName) ? "Segoe UI" : familyName;
                Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily(name);
            }
            catch
            {
                Current.Resources["AppFontFamily"] = new System.Windows.Media.FontFamily("Segoe UI");
            }
        }

        // ── Helper: get a brush by resource key ───────────────────────────────
        public static SolidColorBrush GetBrush(string key)
        {
            if (Current.Resources.Contains(key) && Current.Resources[key] is SolidColorBrush b)
                return b;
            return new SolidColorBrush(Colors.Gray);
        }

        // ── Category color system (same palette as TimerStudio) ───────────────
        private static readonly Color[] _catPalette = new[]
        {
            Color.FromRgb(0x89, 0xB4, 0xFA), // blue
            Color.FromRgb(0xA6, 0xE3, 0xA1), // green
            Color.FromRgb(0xF3, 0x8B, 0xA8), // red/pink
            Color.FromRgb(0xF9, 0xE2, 0xAF), // amber
            Color.FromRgb(0xCB, 0xA6, 0xF7), // mauve
            Color.FromRgb(0x94, 0xE2, 0xD5), // teal
            Color.FromRgb(0xFA, 0xB3, 0x87), // peach
            Color.FromRgb(0x89, 0xDC, 0xEB), // sky
        };

        public static Color CategoryColor(string category)
        {
            if (string.IsNullOrEmpty(category))
                return Color.FromRgb(0x58, 0x5B, 0x70); // gray — uncategorised

            if (AppPrefs.Instance.CategoryColors.TryGetValue(category, out string hex)
                && !string.IsNullOrEmpty(hex))
            {
                try { return (Color)ColorConverter.ConvertFromString(hex); }
                catch { }
            }
            int idx = Math.Abs(category.GetHashCode()) % _catPalette.Length;
            return _catPalette[idx];
        }

        public static void SetCategoryColor(string category, Color color)
        {
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            AppPrefs.Instance.CategoryColors[category] = hex;
            AppPrefs.Instance.DeferredSave();
        }

        /// <summary>Returns a solid, high-contrast foreground (black or white) for text
        /// drawn on an arbitrary user-chosen background color. Same Rec. 601 luma formula
        /// as StickyNoteWindow.StrokeForBackground, applied without the stroke opacity —
        /// used for category pill text, where the background can be any color the user
        /// picks in the category color dialog.</summary>
        public static Color ReadableTextColor(Color bg)
        {
            // WCAG 1.4.6: pick whichever of black/white has the higher contrast ratio against the
            // fill (relative luminance, not the old 0.5 luma cut-off that put white on mid-tones at <4.5:1).
            static double Lin(byte c) { double s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            double l = 0.2126 * Lin(bg.R) + 0.7152 * Lin(bg.G) + 0.0722 * Lin(bg.B);
            double vsBlack = (l + 0.05) / 0.05, vsWhite = 1.05 / (l + 0.05);
            return vsBlack >= vsWhite ? Colors.Black : Colors.White;
        }

        /// <summary>Returns all persisted categories plus any in-use on items.</summary>
        public static List<string> GetAllCategories(
            System.Collections.Generic.IEnumerable<ClipboardItem> clips  = null,
            System.Collections.Generic.IEnumerable<StickyNote>    notes  = null,
            System.Collections.Generic.IEnumerable<TaskItem>      tasks  = null)
        {
            var set = new HashSet<string>(AppPrefs.Instance.Categories, StringComparer.OrdinalIgnoreCase);
            if (clips != null) foreach (var c in clips) if (!string.IsNullOrEmpty(c.Category)) set.Add(c.Category);
            if (notes != null) foreach (var n in notes) if (!string.IsNullOrEmpty(n.Category)) set.Add(n.Category);
            if (tasks != null) foreach (var t in tasks) if (!string.IsNullOrEmpty(t.Category)) set.Add(t.Category);
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}
