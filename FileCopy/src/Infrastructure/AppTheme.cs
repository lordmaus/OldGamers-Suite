using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

#nullable enable

namespace FileCopy
{
    /// <summary>
    /// Manages runtime theme selection and DWM chrome tinting.
    /// "Auto" follows the Windows AppsUseLightTheme registry key (Win10 1903+).
    /// High-contrast always overrides any user preference.
    /// </summary>
    public static class AppTheme
    {
        // ── Theme registry ────────────────────────────────────────────────────

        /// <summary>Describes a selectable theme.</summary>
        public sealed record ThemeInfo(
            string Key,         // stored in AppPrefs
            string DisplayName, // shown in UI
            string? File,       // xaml filename under src/Themes/; null = Auto
            bool   IsDark);     // used by Auto mode to pick matching default

        public static readonly IReadOnlyList<ThemeInfo> AllThemes = new[]
        {
            // ── Pseudo-theme: follow OS ──────────────────────────────────────
            new ThemeInfo("Auto", "Auto (follows Windows)", null, true),
            // ── Dark ──────────────────────────────────────────────
            new ThemeInfo("Dark", "Dark", "Dark.xaml", true),
            new ThemeInfo("OneDark", "One Dark", "OneDark.xaml", true),
            new ThemeInfo("GruvboxDark", "Gruvbox Dark", "GruvboxDark.xaml", true),
            new ThemeInfo("TokyoNight", "Tokyo Night", "TokyoNight.xaml", true),
            new ThemeInfo("Nord", "Nord", "Nord.xaml", true),
            new ThemeInfo("Dracula", "Dracula", "Dracula.xaml", true),
            new ThemeInfo("CatppuccinMocha", "Catppuccin Mocha", "CatppuccinMocha.xaml", true),
            new ThemeInfo("RosePine", "Rosé Pine", "RosePine.xaml", true),
            new ThemeInfo("AyuDark", "Ayu Dark", "AyuDark.xaml", true),
            new ThemeInfo("AyuMirage", "Ayu Mirage", "AyuMirage.xaml", true),
            new ThemeInfo("Cobalt2", "Cobalt 2", "Cobalt2.xaml", true),
            new ThemeInfo("Monokai", "Monokai", "Monokai.xaml", true),
            new ThemeInfo("SolarizedDark", "Solarized Dark", "SolarizedDark.xaml", true),
            new ThemeInfo("Kanagawa", "Kanagawa", "Kanagawa.xaml", true),
            new ThemeInfo("EverforestDark", "Everforest Dark", "EverforestDark.xaml", true),
            new ThemeInfo("Oxocarbon", "Oxocarbon", "Oxocarbon.xaml", true),
            new ThemeInfo("Palenight", "Palenight", "Palenight.xaml", true),
            new ThemeInfo("GitHubDark", "GitHub Dark", "GitHubDark.xaml", true),
            new ThemeInfo("DraculaPro", "Dracula Pro", "DraculaPro.xaml", true),
            new ThemeInfo("Horizon", "Horizon", "Horizon.xaml", true),
            new ThemeInfo("Abyss", "Abyss", "Abyss.xaml", true),
            new ThemeInfo("Obsidian", "Obsidian", "Obsidian.xaml", true),
            new ThemeInfo("Panda", "Panda", "Panda.xaml", true),
            new ThemeInfo("Monochrome", "Monochrome", "Monochrome.xaml", true),
            // ── Light ──────────────────────────────────────────────
            new ThemeInfo("Light", "Light", "Light.xaml", false),
            new ThemeInfo("WarmLight", "Warm Light", "WarmLight.xaml", false),
            new ThemeInfo("CatppuccinLatte", "Catppuccin Latte", "CatppuccinLatte.xaml", false),
            new ThemeInfo("GitHubLight", "GitHub Light", "GitHubLight.xaml", false),
            new ThemeInfo("AyuLight", "Ayu Light", "AyuLight.xaml", false),
            new ThemeInfo("SolarizedLight", "Solarized Light", "SolarizedLight.xaml", false),
            new ThemeInfo("RosePineDawn", "Rose Pine Dawn", "RosePineDawn.xaml", false),
            new ThemeInfo("EverforestLight", "Everforest Light", "EverforestLight.xaml", false),
            new ThemeInfo("PaperWhite", "Paper White", "PaperWhite.xaml", false),
            new ThemeInfo("Sepia", "Sepia", "Sepia.xaml", false),
            new ThemeInfo("Arctic", "Arctic", "Arctic.xaml", false),
            new ThemeInfo("Ivory", "Ivory", "Ivory.xaml", false),
            // ── Contrast ──────────────────────────────────────────────
            new ThemeInfo("HighContrast", "High Contrast", "HighContrast.xaml", true),
            new ThemeInfo("HighContrastLight", "HC Light", "HighContrastLight.xaml", false),
            new ThemeInfo("HCBlue", "HC Blue", "HCBlue.xaml", true),
            new ThemeInfo("HCGreen", "HC Green", "HCGreen.xaml", true),
            new ThemeInfo("HCAmber", "HC Amber", "HCAmber.xaml", true),
            // ── Neon ──────────────────────────────────────────────
            new ThemeInfo("Synthwave", "Synthwave", "Synthwave.xaml", true),
            new ThemeInfo("Cyberpunk", "Cyberpunk", "Cyberpunk.xaml", true),
            new ThemeInfo("NeonCity", "Neon City", "NeonCity.xaml", true),
            new ThemeInfo("VaporWave", "VaporWave", "VaporWave.xaml", true),
            new ThemeInfo("Outrun", "Outrun", "Outrun.xaml", true),
            new ThemeInfo("HoloBlue", "Holo Blue", "HoloBlue.xaml", true),
            new ThemeInfo("Matrix", "Matrix", "Matrix.xaml", true),
            new ThemeInfo("NeonPurple", "Neon Purple", "NeonPurple.xaml", true),
            new ThemeInfo("LaserGrid", "Laser Grid", "LaserGrid.xaml", true),
            new ThemeInfo("ElectricOcean", "Electric Ocean", "ElectricOcean.xaml", true),
            // ── Nature ──────────────────────────────────────────────
            new ThemeInfo("Forest", "Forest", "Forest.xaml", true),
            new ThemeInfo("Ocean", "Ocean", "Ocean.xaml", true),
            new ThemeInfo("DesertSand", "Desert Sand", "DesertSand.xaml", true),
            new ThemeInfo("Autumn", "Autumn", "Autumn.xaml", true),
            new ThemeInfo("MidnightGarden", "Midnight Garden", "MidnightGarden.xaml", true),
            new ThemeInfo("LavenderField", "Lavender Field", "LavenderField.xaml", true),
            new ThemeInfo("CoralReef", "Coral Reef", "CoralReef.xaml", true),
            new ThemeInfo("Tundra", "Tundra", "Tundra.xaml", true),
            new ThemeInfo("Volcanic", "Volcanic", "Volcanic.xaml", true),
            new ThemeInfo("Meadow", "Meadow", "Meadow.xaml", true),
            // ── Retro ──────────────────────────────────────────────
            new ThemeInfo("C64", "C64", "C64.xaml", true),
            new ThemeInfo("AmberCRT", "Amber CRT", "AmberCRT.xaml", true),
            new ThemeInfo("GreenCRT", "Green CRT", "GreenCRT.xaml", true),
            new ThemeInfo("IBMBlue", "IBM Blue", "IBMBlue.xaml", true),
            new ThemeInfo("AppleII", "Apple II", "AppleII.xaml", true),
            new ThemeInfo("BIOS", "BIOS", "BIOS.xaml", true),
            new ThemeInfo("Teletext", "Teletext", "Teletext.xaml", true),
            new ThemeInfo("TerminalRed", "Terminal Red", "TerminalRed.xaml", true),
            new ThemeInfo("VGAPurple", "VGA Purple", "VGAPurple.xaml", true),
            new ThemeInfo("ZXSpectrum", "ZX Spectrum", "ZXSpectrum.xaml", true),
            // ── Warm ──────────────────────────────────────────────
            new ThemeInfo("Ember", "Ember", "Ember.xaml", true),
            new ThemeInfo("Rosewood", "Rosewood", "Rosewood.xaml", true),
            new ThemeInfo("GoldenHour", "Golden Hour", "GoldenHour.xaml", true),
            new ThemeInfo("Terracotta", "Terracotta", "Terracotta.xaml", true),
            new ThemeInfo("Rust", "Rust", "Rust.xaml", true),
            new ThemeInfo("Mahogany", "Mahogany", "Mahogany.xaml", true),
            new ThemeInfo("Cinnamon", "Cinnamon", "Cinnamon.xaml", true),
            new ThemeInfo("Wine", "Wine", "Wine.xaml", true),
            new ThemeInfo("Copper", "Copper", "Copper.xaml", true),
            new ThemeInfo("Sundowner", "Sundowner", "Sundowner.xaml", true),
            // ── Cool ──────────────────────────────────────────────
            new ThemeInfo("MidnightBlue", "Midnight Blue", "MidnightBlue.xaml", true),
            new ThemeInfo("DeepSpace", "Deep Space", "DeepSpace.xaml", true),
            new ThemeInfo("Glacier", "Glacier", "Glacier.xaml", true),
            new ThemeInfo("Cobalt", "Cobalt", "Cobalt.xaml", true),
            new ThemeInfo("Sapphire", "Sapphire", "Sapphire.xaml", true),
            new ThemeInfo("Periwinkle", "Periwinkle", "Periwinkle.xaml", true),
            new ThemeInfo("ArcticFox", "Arctic Fox", "ArcticFox.xaml", true),
            new ThemeInfo("Steel", "Steel", "Steel.xaml", true),
            new ThemeInfo("Slate", "Slate", "Slate.xaml", true),
            new ThemeInfo("IndigoNight", "Indigo Night", "IndigoNight.xaml", true),
            // ── Pastel ──────────────────────────────────────────────
            new ThemeInfo("CottonCandy", "Cotton Candy", "CottonCandy.xaml", true),
            new ThemeInfo("MintCream", "Mint Cream", "MintCream.xaml", true),
            new ThemeInfo("BabyBlue", "Baby Blue", "BabyBlue.xaml", true),
            new ThemeInfo("Peach", "Peach", "Peach.xaml", true),
            new ThemeInfo("Lilac", "Lilac", "Lilac.xaml", true),
            new ThemeInfo("Buttercup", "Buttercup", "Buttercup.xaml", true),
            new ThemeInfo("Blush", "Blush", "Blush.xaml", true),
            new ThemeInfo("Seafoam", "Seafoam", "Seafoam.xaml", true),
            new ThemeInfo("Wisteria", "Wisteria", "Wisteria.xaml", true),
            new ThemeInfo("Dusk", "Dusk", "Dusk.xaml", true),
            // ── Specialty ──────────────────────────────────────────────
            new ThemeInfo("CoffeeShop", "Coffee Shop", "CoffeeShop.xaml", true),
            new ThemeInfo("Denim", "Denim", "Denim.xaml", true),
            new ThemeInfo("Malachite", "Malachite", "Malachite.xaml", true),
            new ThemeInfo("SolarFlare", "Solar Flare", "SolarFlare.xaml", true),
            new ThemeInfo("Moonstone", "Moonstone", "Moonstone.xaml", true),
            new ThemeInfo("Bubblegum", "Bubblegum", "Bubblegum.xaml", true),
        };

        /// <summary>Valid theme keys, including "Auto".</summary>
        public static readonly IReadOnlySet<string> ValidKeys =
            new HashSet<string>(AllThemes.Select(t => t.Key));

        // ── Public API ────────────────────────────────────────────────────────
        public static void Apply()        => ApplyTheme();
        public static bool SystemIsDark() => IsSystemDarkMode();

        private static CancellationTokenSource? _watchCts;

        /// <summary>
        /// Polls OS dark-mode every 5 s; re-applies only when Auto is active and
        /// the system preference actually changes. Call StopAutoWatch() on app exit.
        /// </summary>
        public static void StartAutoWatch()
        {
            _watchCts = new CancellationTokenSource();
            var ct = _watchCts.Token;
            _ = Task.Run(async () =>
            {
                bool? lastDark = null;
                while (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(5000, ct); }
                    catch (OperationCanceledException) { return; }
                    if (AppPrefs.Instance.Theme != "Auto") continue;

                    bool nowDark = IsSystemDarkMode();
                    if (nowDark == lastDark) continue;
                    lastDark = nowDark;
                    Application.Current?.Dispatcher.InvokeAsync(ApplyTheme);
                }
            });
        }

        /// <summary>
        /// Stops the auto-watch background task. Call from App.OnExit.
        /// </summary>
        public static void StopAutoWatch() => _watchCts?.Cancel();

        // ── Implementation ────────────────────────────────────────────────────
        private static void ApplyTheme()
        {
            var dict = Application.Current?.Resources?.MergedDictionaries;
            if (dict == null) return;

            // High-contrast always wins
            if (SystemParameters.HighContrast)
            {
                ReplaceThemeDict(dict, "src/Themes/HighContrast.xaml");
                DwmSync(dark: false);
                return;
            }

            string pref = AppPrefs.Instance.Theme;
            ThemeInfo? info = AllThemes.FirstOrDefault(t => t.Key == pref);

            if (info == null || info.File == null)
            {
                // Auto or unrecognised key — fall back to system preference
                bool dark = IsSystemDarkMode();
                info = dark
                    ? AllThemes.First(t => t.Key == "Dark")
                    : AllThemes.First(t => t.Key == "Light");
            }

            ReplaceThemeDict(dict, $"src/Themes/{info.File}");
            DwmSync(info.IsDark);
        }

        private static void DwmSync(bool dark)
        {
            if (Application.Current?.MainWindow is MainWindow mw && mw.IsLoaded)
                DwmHelpers.EnableDarkChrome(mw, dark);
        }

        /// <summary>
        /// Atomic swap of the active theme ResourceDictionary — avoids the
        /// single-frame white flash caused by a remove+add pair.
        /// </summary>
        private static void ReplaceThemeDict(
            System.Collections.ObjectModel.Collection<ResourceDictionary> dicts,
            string themeRelativeUri)
        {
            var newDict = new ResourceDictionary
            {
                Source = new Uri(themeRelativeUri, UriKind.Relative)
            };

            int idx = -1;
            for (int i = 0; i < dicts.Count; i++)
            {
                var src = dicts[i].Source?.OriginalString ?? "";
                if (src.Contains("Themes/") && src.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                { idx = i; break; }
            }

            if (idx >= 0)
                dicts[idx] = newDict;
            else
                dicts.Add(newDict);
        }

        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;
            }
            catch { return true; }
        }
    }
}
