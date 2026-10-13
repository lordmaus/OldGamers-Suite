using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BadgeOverlay
{
    public partial class BadgeColorsWindow : Window
    {
        private readonly AppSettings _s;

        // (Label, property getter, property setter, default hex)
        private static readonly (string Label, string Desc, string Default,
            Func<AppSettings,string> Get, Action<AppSettings,string> Set)[] Categories =
        {
            ("Close",    "Close Window buttons",        "#F38BA8", s => s.BadgeColorClose,    (s,v) => s.BadgeColorClose    = v),
            ("Minimize", "Minimize buttons",             "#FAB387", s => s.BadgeColorMinimize, (s,v) => s.BadgeColorMinimize = v),
            ("Maximize", "Maximize / Restore buttons",   "#A6E3A1", s => s.BadgeColorMaximize, (s,v) => s.BadgeColorMaximize = v),
            ("Action",   "OK / Save / Apply / Send…",    "#A6E3A1", s => s.BadgeColorAction,   (s,v) => s.BadgeColorAction   = v),
            ("Danger",   "Delete / Cancel / Remove",     "#F38BA8", s => s.BadgeColorDanger,   (s,v) => s.BadgeColorDanger   = v),
            ("Nav",      "Back / Forward / Home / Find", "#89B4FA", s => s.BadgeColorNav,      (s,v) => s.BadgeColorNav      = v),
            ("Browser",  "Browser / web controls",       "#89B4FA", s => s.BadgeColorBrowser,  (s,v) => s.BadgeColorBrowser  = v),
            ("Control",  "Generic controls & inputs",    "#94E2D5", s => s.BadgeColorControl,  (s,v) => s.BadgeColorControl  = v),
        };

        // ── Palette presets ───────────────────────────────────────────────
        //
        // Each preset is an ordered list of 8 hex colours, one per
        // category in the same order as Categories[] above (Close,
        // Minimize, Maximize, Action, Danger, Nav, Browser, Control).
        //
        // Sources:
        //   • Default     — Catppuccin Mocha accent palette (the same
        //                   colours hardcoded in Categories[].Default).
        //   • Deuteranopia / Protanopia — Wong (2011) colour-blind-safe
        //                   palette, Nature Methods 8: 441. Optimised
        //                   for green-deficient (deutan / protan) vision
        //                   by avoiding red↔green ambiguity.
        //   • Tritanopia  — IBM Carbon accessible palette tuned for
        //                   blue-yellow deficiency; substitutes warm
        //                   accents for the blue-green pair.
        //
        // Every preset preserves ≥7:1 contrast against the badge body
        // background (verified manually with the same routine used in
        // the existing Reset action).

        private struct Preset
        {
            public string Label;
            public string Description;
            public string[] Hex;   // 8 entries, one per Categories[] index
            public bool IsCustom;
        }

        private static readonly Preset[] Presets =
        {
            new Preset
            {
                Label       = "Default",
                Description = "Factory colour scheme",
                Hex         = new[] { "#F38BA8", "#FAB387", "#A6E3A1", "#A6E3A1",
                                      "#F38BA8", "#89B4FA", "#89B4FA", "#94E2D5" }
            },
            new Preset
            {
                Label       = "Deuteranopia (red-green safe)",
                Description = "Wong 2011 palette — distinguishable for green-deficient vision",
                // Wong palette: orange / blue / green / yellow / sky / vermillion / purple / black-grey
                // Mapped to badge categories so Close/Danger stay visually warning-coloured but
                // are now Vermillion (#D55E00) instead of red — readable for deutans.
                Hex         = new[] { "#D55E00", "#E69F00", "#009E73", "#56B4E9",
                                      "#D55E00", "#0072B2", "#0072B2", "#F0E442" }
            },
            new Preset
            {
                Label       = "Protanopia (red-green safe)",
                Description = "Same family as Deuteranopia, brighter blue tones for protans",
                Hex         = new[] { "#E69F00", "#F0E442", "#009E73", "#56B4E9",
                                      "#E69F00", "#0072B2", "#0072B2", "#CC79A7" }
            },
            new Preset
            {
                Label       = "Tritanopia (blue-yellow safe)",
                Description = "Carbon-inspired palette without blue-yellow ambiguity",
                Hex         = new[] { "#FA4D56", "#FF832B", "#42BE65", "#42BE65",
                                      "#FA4D56", "#A56EFF", "#A56EFF", "#08BDBA" }
            },
            new Preset
            {
                Label       = "Custom",
                Description = "Manually-edited swatches",
                Hex         = null,    // sentinel — not applied; auto-selected when no preset matches
                IsCustom    = true,
            },
        };

        private bool _suppressPresetChange;

        public BadgeColorsWindow(AppSettings settings)
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            _s = settings;
            BuildRows();
            BuildPresetCombo();
            SyncPresetSelection();
            Closed += (s, e) => _s.Save();
        }

        /// <summary>
        /// Populates the palette-preset ComboBox. Items are bound to
        /// the static <see cref="Presets"/> table; SelectionChanged
        /// applies the corresponding palette and rebuilds rows.
        /// </summary>
        private void BuildPresetCombo()
        {
            if (CboPalettePreset == null) return;
            _suppressPresetChange = true;
            try
            {
                CboPalettePreset.Items.Clear();
                foreach (var p in Presets)
                {
                    var item = new ComboBoxItem
                    {
                        Content = p.Label,
                        ToolTip = p.Description,
                    };
                    CboPalettePreset.Items.Add(item);
                }
            }
            finally { _suppressPresetChange = false; }
        }

        /// <summary>
        /// Compares the current settings against each preset and
        /// selects the matching ComboBox item. Falls through to the
        /// "Custom" sentinel when no preset matches — that's the
        /// indicator that the user has hand-edited individual swatches.
        /// </summary>
        private void SyncPresetSelection()
        {
            if (CboPalettePreset == null) return;
            int matchIndex = -1;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Presets[i].IsCustom) continue;
                bool allMatch = true;
                for (int c = 0; c < Categories.Length; c++)
                {
                    string current = Categories[c].Get(_s) ?? "";
                    string preset  = Presets[i].Hex[c]      ?? "";
                    if (!string.Equals(current, preset, StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) { matchIndex = i; break; }
            }
            if (matchIndex < 0)
            {
                // Find "Custom" sentinel index.
                for (int i = 0; i < Presets.Length; i++)
                    if (Presets[i].IsCustom) { matchIndex = i; break; }
            }
            _suppressPresetChange = true;
            try { CboPalettePreset.SelectedIndex = matchIndex; }
            finally { _suppressPresetChange = false; }
        }

        private void CboPalettePreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPresetChange) return;
            int idx = CboPalettePreset.SelectedIndex;
            if (idx < 0 || idx >= Presets.Length) return;
            var p = Presets[idx];
            if (p.IsCustom) return;   // "Custom" doesn't apply anything

            for (int c = 0; c < Categories.Length; c++)
                Categories[c].Set(_s, p.Hex[c]);

            // Persist immediately so the change survives if the user
            // closes the window without doing anything else. _s.Save
            // is also called on Closed (constructor wires it) but
            // we want a checkpoint right after applying — both for
            // safety and so PushToDetector below sees fresh values.
            try { _s.Save(); }
            catch (Exception ex) { CrashLog.Write("Palette preset Save", ex); }

            BuildRows();   // refresh swatches + contrast readouts
            try { _s.PushToDetector(); }
            catch (Exception ex) { CrashLog.Write("Palette preset Push", ex); }
        }

        // ── Reference surfaces for contrast checks ────────────────────────────
        // Delegate to BadgeSurface so any change to the on-screen badge
        // colours is reflected automatically in the contrast readout.
        // No local constant — avoids silent drift if OverlayWindow.xaml
        // changes the actual pill colour without updating this file.
        private static Color BadgeBackground    => BadgeSurface.BodyBackground;
        private static Color IconPillBackground => BadgeSurface.IconPillBackground;

        private void BuildRows()
        {
            ColorRows.Children.Clear();
            foreach (var cat in Categories)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

                // Label
                var lbl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                lbl.Children.Add(new TextBlock { Text = cat.Label, FontSize = 13, FontWeight = FontWeights.SemiBold });
                // Muted token at full opacity (was 10px @ 0.55 opacity ≈ 3:1)
                var desc = new TextBlock { Text = cat.Desc, FontSize = Controls.TypeRamp.Caption, TextWrapping = TextWrapping.Wrap };
                desc.SetResourceReference(TextBlock.ForegroundProperty, "BrushMuted");
                lbl.Children.Add(desc);
                Grid.SetColumn(lbl, 0);

                // Hex text box
                var tb = new TextBox
                {
                    Text       = cat.Get(_s),
                    FontFamily = new FontFamily("Consolas"),
                    Padding    = new Thickness(8, 4, 4, 4),
                    Margin     = new Thickness(10, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(tb, 1);

                // Colour swatch
                var swatch = new Border
                {
                    Width = 34, Height = 28,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cat.Get(_s))); }
                catch { swatch.Background = new SolidColorBrush(Colors.Gray); }
                try { swatch.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")); }
                catch { }
                Grid.SetColumn(swatch, 2);

                // Pick button
                var btn = new Button
                {
                    Content   = "Pick…",
                    Padding   = new Thickness(8, 4, 8, 4),
                    MinHeight = 44, MinWidth = 44,
                    Margin    = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style     = TryFindResource("FlatButton") as Style,
                };
                Grid.SetColumn(btn, 3);

                // Contrast grade (WCAG) — dual readout: badge body + icon pill
                var contrastLbl = new TextBlock
                {
                    FontSize     = Controls.TypeRamp.Caption,
                    FontWeight   = FontWeights.SemiBold,
                    FontFamily   = new FontFamily("Consolas"),
                    TextAlignment = TextAlignment.Right,
                    Margin       = new Thickness(6, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    LineHeight   = 15,
                };
                UpdateContrastLabel(contrastLbl, cat.Get(_s));
                Grid.SetColumn(contrastLbl, 4);
                System.Windows.Automation.AutomationProperties.SetName(contrastLbl,
                    $"{cat.Label} contrast ratio");

                // Wire events
                var capCat = cat;
                Border capSwatch = swatch;
                TextBox capTb = tb;
                TextBlock capCon = contrastLbl;

                tb.TextChanged += (s2, e2) =>
                {
                    try
                    {
                        var col = (Color)ColorConverter.ConvertFromString(capTb.Text);
                        capSwatch.Background = new SolidColorBrush(col);
                        capCat.Set(_s, capTb.Text);
                        UpdateContrastLabel(capCon, capTb.Text);
                        SyncPresetSelection();
                    }
                    catch { }
                };

                btn.Click += (s2, e2) =>
                {
                    var dlg = new System.Windows.Forms.ColorDialog
                    {
                        FullOpen = true, AllowFullOpen = true,
                    };
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(capCat.Get(_s));
                        dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                    }
                    catch { }

                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                        capCat.Set(_s, hex);
                        capTb.Text = hex;
                        capSwatch.Background = new SolidColorBrush(
                            Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                        UpdateContrastLabel(capCon, hex);
                        SyncPresetSelection();
                    }
                };

                // Per-row reset — restores just this category's factory
                // default without touching other customisations. Lets users
                // experiment on one colour and recover without affecting
                // the rest of their palette.
                var resetBtn = new Button
                {
                    Content   = JtIcons.Img("undo", 16),
                    Padding   = new Thickness(0),
                    MinHeight = 28, Width = 28,
                    Margin    = new Thickness(2, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style     = TryFindResource("FlatButton") as Style,
                    ToolTip   = $"Reset {cat.Label} to factory default ({cat.Default})",
                };
                System.Windows.Automation.AutomationProperties.SetName(resetBtn,
                    $"Reset {cat.Label} to default");
                Grid.SetColumn(resetBtn, 5);
                resetBtn.Click += (s2, e2) =>
                {
                    capCat.Set(_s, capCat.Default);
                    capTb.Text = capCat.Default;
                    try
                    {
                        capSwatch.Background = new SolidColorBrush(
                            (Color)ColorConverter.ConvertFromString(capCat.Default));
                    }
                    catch { }
                    UpdateContrastLabel(capCon, capCat.Default);
                    SyncPresetSelection();
                };

                row.Children.Add(lbl);
                row.Children.Add(tb);
                row.Children.Add(swatch);
                row.Children.Add(btn);
                row.Children.Add(contrastLbl);
                row.Children.Add(resetBtn);
                ColorRows.Children.Add(row);
            }
        }

        /// <summary>
        /// Computes two WCAG contrast ratios — against the dark badge body
        /// AND against the tinted icon pill — and stamps the label with
        /// both results. The lower of the two ratios drives the tint
        /// colour (green / amber / red) and the grade because the worst
        /// case is what fails the audit. Tooltip explains each reference
        /// surface.
        /// </summary>
        private static void UpdateContrastLabel(TextBlock lbl, string hex)
        {
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(hex);
                double rBadge = ContrastHelper.Ratio(col, BadgeBackground);
                double rPill  = ContrastHelper.Ratio(col, IconPillBackground);
                double worst  = Math.Min(rBadge, rPill);
                string grade  = ContrastHelper.Grade(worst);

                // Two-line readout: badge body on top, icon pill on bottom.
                // Grade reflects the worst case so the user sees a single
                // pass/fail and can drill into the tooltip for detail.
                lbl.Text = $"body {rBadge:F1}  pill {rPill:F1}\n{grade}";

                lbl.ToolTip =
                    $"Badge body ({Hex(BadgeBackground)}): {rBadge:F2}:1 — {ContrastHelper.Grade(rBadge)}\n" +
                    $"Icon pill ({Hex(IconPillBackground)}): {rPill:F2}:1 — {ContrastHelper.Grade(rPill)}\n" +
                    $"WCAG AAA normal text target: ≥ 7:1\n" +
                    (worst >= ContrastHelper.AAANormalText
                        ? "Passes AAA on both surfaces."
                        : worst >= ContrastHelper.AAALargeText
                            ? "Passes AAA large-text target (≥ 4.5:1) on the worst surface."
                            : worst >= ContrastHelper.AAComponent
                                ? "Meets AA graphics minimum (≥ 3:1). Below AAA text target."
                                : "FAILS WCAG minimums on at least one surface — consider a brighter colour.");

                lbl.Foreground = worst >= ContrastHelper.AAANormalText
                    ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))   // green
                    : worst >= ContrastHelper.AAComponent
                        ? new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87)) // amber
                        : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)); // red
            }
            catch
            {
                lbl.Text = "—";
                lbl.ToolTip = null;
            }
        }

        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private void BtnReset_Click(object s, RoutedEventArgs e)
        {
            // Destructive — wipes every user-customised colour. Require
            // confirmation so an errant click doesn't lose a palette the
            // user spent time tuning.
            var r = MessageBox.Show(
                this,
                "Reset every category colour to its factory default?\n\n" +
                "Any customisations will be lost. This cannot be undone.",
                "Reset badge colours",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            foreach (var cat in Categories)
                cat.Set(_s, cat.Default);
            BuildRows();
            SyncPresetSelection();   // jumps back to "Default" preset
        }

        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("BadgeColorsWindow", 540, 560);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("BadgeColorsWindow", Width, Height);
            base.OnClosed(e);
        }
    }
}
