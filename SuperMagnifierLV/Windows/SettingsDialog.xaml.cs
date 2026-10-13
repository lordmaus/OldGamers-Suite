using System;
using System.Windows;
using System.Windows.Threading;

namespace SuperMagnifier
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettings _live;
        private readonly Action<AppSettings> _applyCallback;

        // Set by MainWindow so the "← Use current" buttons work
        public double CurrentZoom  { get; set; }
        public int    CurrentLensW { get; set; }
        public int    CurrentLensH { get; set; }

        public SettingsDialog(AppSettings live, Action<AppSettings> applyCallback)
        {
            InitializeComponent();
            _live          = live;
            _applyCallback = applyCallback;

            // WCAG 2.4.6: section headers get descriptive names for screen readers.
            // Set heading level for screen reader section navigation.
            var sectionNames = new (System.Windows.Controls.TextBlock tb, string name)[]
            {
                (HeadLensSize,      "Section: Lens Size"),
                (HeadZoom,          "Section: Zoom"),
                (HeadRendering,     "Section: Rendering Quality"),
                (HeadBehaviour,     "Section: Behaviour"),
                (HeadZoomStep,      "Section: Zoom Step"),
                (HeadLensAppearance,"Section: Lens Appearance"),
                (HeadSmoothPanning, "Section: Smooth Panning"),
                (HeadDimWhenIdle,   "Section: Dim When Idle"),
                (HeadStartupDefaults, "Section: Startup Defaults"),
                (HeadDisplayFonts,    "Section: Display and Fonts"),
            };
            foreach (var (tb, name) in sectionNames)
                System.Windows.Automation.AutomationProperties.SetName(tb, name);

            // ── Lens size / zoom ────────────────────────────────────────────
            LensWBox.Text = live.DefaultLensW.ToString();
            LensHBox.Text = live.DefaultLensH.ToString();
            ZoomBox.Text  = live.DefaultZoom.ToString("F2");

            // ── Rendering quality ───────────────────────────────────────────
            SmoothRadio.IsChecked = live.RenderMode == "smooth";
            CrispRadio.IsChecked  = live.RenderMode == "crisp";

            // ── Behaviour checkboxes ────────────────────────────────────────
            AotCheck.IsChecked            = live.AlwaysOnTop;
            AutoStartCheck.IsChecked      = live.AutoStart;
            SessionRestoreCheck.IsChecked = live.SessionRestore;
            HotkeyCheck.IsChecked              = live.HotkeyToggle;
            InvertScrollCheck.IsChecked        = live.InvertScrollZoom;
            ZoomAnimateCheck.IsChecked         = live.ZoomAnimate;
            OpacityFollowZoomCheck.IsChecked   = live.OpacityFollowZoom;
            // ── Zoom step ───────────────────────────────────────────────────
            Step01.IsChecked  = live.Step == 0.1;
            Step05.IsChecked  = live.Step == 0.5;
            Step1.IsChecked   = live.Step == 1.0;
            Step2.IsChecked   = live.Step == 2.0;
            // 0.25 is the fallback for any unrecognised value
            if (live.Step != 0.1 && live.Step != 0.5 && live.Step != 1.0 && live.Step != 2.0)
                Step025.IsChecked = true;
            else
                Step025.IsChecked = live.Step == 0.25;

            // ── Lens appearance ─────────────────────────────────────────────
            OpacitySlider.Value = Math.Max(0.2, Math.Min(1.0, live.LensOpacity));
            OpacityLabel.Text   = $"{(int)(live.LensOpacity * 100)}%";
            // Select the current theme in the ComboBox (match by Tag)
            foreach (System.Windows.Controls.ComboBoxItem item in ThemeCombo.Items)
            {
                if (item.Tag?.ToString() == App.CurrentTheme)
                {
                    ThemeCombo.SelectedItem = item;
                    break;
                }
            }

            // ── Smooth panning ──────────────────────────────────────────────
            SmoothPanCheck.IsChecked = live.SmoothPanning;
            PanLagSlider.Value       = Math.Max(0.03, Math.Min(0.45, live.PanLag));
            UpdatePanLagLabel(live.PanLag);

            // ── Dim when idle ───────────────────────────────────────────────
            DimWhenIdleCheck.IsChecked = live.DimWhenIdle;
            DimDelaySlider.Value       = Math.Max(2, Math.Min(30, live.DimDelaySeconds));
            DimDelayLabel.Text         = $"{live.DimDelaySeconds}s";
            DimOpacitySlider.Value     = Math.Max(0.05, Math.Min(0.9, live.DimOpacity));
            DimOpacityLabel.Text       = $"{(int)(live.DimOpacity * 100)}%";

            // ── Startup defaults ────────────────────────────────────────────
            DefaultFollowMouseRadio.IsChecked  = live.DefaultFollowMode != "caret";
            DefaultFollowCaretRadio.IsChecked  = live.DefaultFollowMode == "caret";
            DefaultSnapToWordCheck.IsChecked   = live.DefaultSnapToWord;
            DefaultReticleCheck.IsChecked      = live.DefaultReticle;
            // -1 = Remember last (index 0 in combo); 0/1/2/3 = Small/Normal/Large/XL (index 1-4)
            DefaultUiSizeCombo.SelectedIndex =
                live.DefaultUiSizeIndex < 0 ? 0 : Math.Min(live.DefaultUiSizeIndex + 1, 4);
            // Always applied at startup — no "remember last" entry; index maps 1:1 to size.
            DefaultTitleBarSizeCombo.SelectedIndex =
                Math.Max(0, Math.Min(live.DefaultTitleBarSizeIndex, 4));

            // Lens bar text visibility
            BarHideAllTextCheck.IsChecked         = live.BarHideAllText;
            BarShowSizeLabelCheck.IsChecked       = live.BarShowSizeLabel;
            BarShowColourLabelCheck.IsChecked     = live.BarShowColourLabel;
            BarShowSnapLabelCheck.IsChecked       = live.BarShowSnapLabel;
            BarShowClickThruLabelCheck.IsChecked  = live.BarShowClickThruLabel;
            BarShowFreezeLabelCheck.IsChecked     = live.BarShowFreezeLabel;
            BarShowZoomValueCheck.IsChecked       = live.BarShowZoomValue;
            BarShowHoverHintCheck.IsChecked       = live.BarShowHoverHint;

            // ── Display & Fonts (Low-Vision) ────────────────────────────────
            PopulateFontFamilyCombo(live.AppFontFamily);
            double hud = Math.Clamp(live.HudFontSize <= 0 ? 12 : live.HudFontSize, 8, 72);
            SldHudFontSize.Value   = hud;
            CboToastDuration.SelectedIndex = live.ToastSeconds switch { 10 => 1, 0 => 2, _ => 0 };
            HudFontSizeLabel.Text  = $"{(int)hud} px";
            SldHudFontSize.ValueChanged += (s, ev) =>
            {
                int v = (int)Math.Round(ev.NewValue);
                HudFontSizeLabel.Text = $"{v} px";
                // Live preview so the user sees the effect immediately
                App.SetHudFontSize(v);
            };

            FilePathLabel.Text = $"Saved to: {System.IO.Path.GetFileName(AppSettings.FilePath)}";

            // ── Slider live labels ──────────────────────────────────────────
            OpacitySlider.ValueChanged    += (s, ev) => OpacityLabel.Text    = $"{(int)(ev.NewValue * 100)}%";
            PanLagSlider.ValueChanged     += (s, ev) => UpdatePanLagLabel(ev.NewValue);
            DimDelaySlider.ValueChanged   += (s, ev) => DimDelayLabel.Text   = $"{(int)ev.NewValue}s";
            DimOpacitySlider.ValueChanged += (s, ev) => DimOpacityLabel.Text = $"{(int)(ev.NewValue * 100)}%";

            Loaded += (s, e) =>
            {
                LensWBox.Text = CurrentLensW > 0 ? CurrentLensW.ToString() : live.DefaultLensW.ToString();
                LensHBox.Text = CurrentLensH > 0 ? CurrentLensH.ToString() : live.DefaultLensH.ToString();
                ZoomBox.Text  = CurrentZoom  > 0 ? CurrentZoom.ToString("F2") : live.DefaultZoom.ToString("F2");
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        // ── Display & Fonts (Low-Vision) ────────────────────────────────────
        // Curated low-vision-friendly families are prepended; the full system
        // list follows. We use ComboBoxItem.Tag = family-name for lookup so the
        // display string stays clean even for "Mocha (Catppuccin)"-style names.
        private static readonly string[] _curatedFontFamilies = new[]
        {
            "Segoe UI",
            "Atkinson Hyperlegible",
            "OpenDyslexic",
            "Verdana",
            "Arial",
            "Tahoma",
            "Calibri",
            "Comic Sans MS",
            "Consolas",
        };

        private void PopulateFontFamilyCombo(string currentFamily)
        {
            CboAppFontFamily.Items.Clear();

            // Curated header
            foreach (var fam in _curatedFontFamilies)
            {
                var item = new System.Windows.Controls.ComboBoxItem
                {
                    Content  = fam,
                    Tag      = fam,
                    FontFamily = new System.Windows.Media.FontFamily(fam),
                };
                CboAppFontFamily.Items.Add(item);
            }

            // Separator
            CboAppFontFamily.Items.Add(new System.Windows.Controls.Separator());

            // All installed system families, alphabetised, skipping any already in the curated list.
            var seen = new System.Collections.Generic.HashSet<string>(
                _curatedFontFamilies, System.StringComparer.OrdinalIgnoreCase);
            var sysFamilies = new System.Collections.Generic.List<string>();
            foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies)
            {
                var src = ff.Source;
                if (string.IsNullOrWhiteSpace(src)) continue;
                if (seen.Contains(src)) continue;
                sysFamilies.Add(src);
            }
            sysFamilies.Sort(System.StringComparer.OrdinalIgnoreCase);
            foreach (var fam in sysFamilies)
            {
                var item = new System.Windows.Controls.ComboBoxItem
                {
                    Content  = fam,
                    Tag      = fam,
                    FontFamily = new System.Windows.Media.FontFamily(fam),
                };
                CboAppFontFamily.Items.Add(item);
            }

            // Select the active family
            string target = string.IsNullOrWhiteSpace(currentFamily) ? "Segoe UI" : currentFamily;
            foreach (var obj in CboAppFontFamily.Items)
            {
                if (obj is System.Windows.Controls.ComboBoxItem ci &&
                    ci.Tag is string tag &&
                    string.Equals(tag, target, System.StringComparison.OrdinalIgnoreCase))
                {
                    CboAppFontFamily.SelectedItem = ci;
                    return;
                }
            }
            // Fallback — select the first item (Segoe UI)
            if (CboAppFontFamily.Items.Count > 0)
                CboAppFontFamily.SelectedIndex = 0;
        }

        private void CboAppFontFamily_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CboAppFontFamily.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                item.Tag is string fam)
            {
                // Live preview
                App.SetAppFontFamily(fam);
            }
        }

        private void UpdatePanLagLabel(double v)
            => PanLagLabel.Text = v < 0.1 ? "Tight" : v < 0.25 ? "Medium" : "Loose";

        // ── Snap buttons ──────────────────────────────────────────────────────
        private void SnapSizeBtn_Click(object sender, RoutedEventArgs e)
        {
            LensWBox.Text = CurrentLensW.ToString();
            LensHBox.Text = CurrentLensH.ToString();
        }

        private void SnapZoomBtn_Click(object sender, RoutedEventArgs e)
            => ZoomBox.Text = CurrentZoom.ToString("F2");

        // ── Theme picker ──────────────────────────────────────────────────────
        private void ThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item
                && item.Tag is string key)
            {
                App.SetTheme(key);
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────
        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseInputs(out int w, out int h, out double zoom))
            {
                StatusLabel.Text       = "Invalid value — check width, height, or zoom.";
                StatusIcon.Visibility  = Visibility.Visible;
                StatusLabel.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            // Clone ALL live settings then overwrite only what this dialog owns.
            var updated = _live.Clone();

            // Lens size / zoom
            updated.Zoom         = zoom;
            updated.LensW        = w;
            updated.LensH        = h;
            updated.DefaultLensW = w;
            updated.DefaultLensH = h;
            updated.DefaultZoom  = zoom;

            // Rendering
            updated.RenderMode = SmoothRadio.IsChecked == true ? "smooth" : "crisp";

            // Behaviour
            updated.AlwaysOnTop      = AotCheck.IsChecked            == true;
            updated.AutoStart        = AutoStartCheck.IsChecked      == true;
            updated.SessionRestore   = SessionRestoreCheck.IsChecked == true;
            updated.HotkeyToggle      = HotkeyCheck.IsChecked             == true;
            updated.InvertScrollZoom  = InvertScrollCheck.IsChecked       == true;
            updated.ZoomAnimate       = ZoomAnimateCheck.IsChecked        == true;
            updated.OpacityFollowZoom = OpacityFollowZoomCheck.IsChecked  == true;
            // Zoom step
            updated.Step = Step01.IsChecked == true ? 0.1
                         : Step05.IsChecked == true ? 0.5
                         : Step1.IsChecked  == true ? 1.0
                         : Step2.IsChecked  == true ? 2.0
                         : 0.25;

            // Lens appearance
            updated.LensOpacity = OpacitySlider.Value;
            updated.DarkMode    = App.IsDark;
            updated.ThemeName   = App.CurrentTheme;

            // Smooth panning
            updated.SmoothPanning = SmoothPanCheck.IsChecked == true;
            updated.PanLag        = PanLagSlider.Value;

            // Dim when idle
            updated.DimWhenIdle     = DimWhenIdleCheck.IsChecked == true;
            updated.DimDelaySeconds = (int)Math.Round(DimDelaySlider.Value);
            updated.DimOpacity      = DimOpacitySlider.Value;

            // Startup defaults
            updated.DefaultFollowMode    = DefaultFollowCaretRadio.IsChecked == true ? "caret" : "mouse";
            updated.DefaultSnapToWord    = DefaultSnapToWordCheck.IsChecked   == true;
            updated.DefaultReticle       = DefaultReticleCheck.IsChecked      == true;
            // Combo index 0 = Remember last (-1), index 1+ = explicit size (subtract 1)
            updated.DefaultUiSizeIndex       = DefaultUiSizeCombo.SelectedIndex <= 0
                                               ? -1 : DefaultUiSizeCombo.SelectedIndex - 1;
            // Always applied at startup — no "remember last" entry; index maps 1:1 to size.
            updated.DefaultTitleBarSizeIndex = Math.Max(0, DefaultTitleBarSizeCombo.SelectedIndex);

            // Lens bar text visibility
            updated.BarHideAllText        = BarHideAllTextCheck.IsChecked        == true;
            updated.BarShowSizeLabel      = BarShowSizeLabelCheck.IsChecked      == true;
            updated.BarShowColourLabel    = BarShowColourLabelCheck.IsChecked    == true;
            updated.BarShowSnapLabel      = BarShowSnapLabelCheck.IsChecked      == true;
            updated.BarShowClickThruLabel = BarShowClickThruLabelCheck.IsChecked == true;
            updated.BarShowFreezeLabel    = BarShowFreezeLabelCheck.IsChecked    == true;
            updated.BarShowZoomValue      = BarShowZoomValueCheck.IsChecked      == true;
            updated.BarShowHoverHint      = BarShowHoverHintCheck.IsChecked      == true;

            // Display & Fonts (Low-Vision)
            string famSel = "Segoe UI";
            if (CboAppFontFamily.SelectedItem is System.Windows.Controls.ComboBoxItem ci
                && ci.Tag is string ft && !string.IsNullOrWhiteSpace(ft))
                famSel = ft;
            updated.AppFontFamily = famSel;
            updated.HudFontSize   = Math.Clamp(SldHudFontSize.Value, 8, 72);
            updated.ToastSeconds  = CboToastDuration.SelectedIndex switch { 1 => 10, 2 => 0, _ => 5 };
            App.SetAppFontFamily(updated.AppFontFamily);
            App.SetHudFontSize(updated.HudFontSize);

            updated.Save();
            _applyCallback?.Invoke(updated);

            StatusLabel.Foreground = (System.Windows.Media.Brush)
                Application.Current.Resources["BrushGreenText"];
            StatusLabel.Text = "✓  Settings saved!";
            StatusIcon.Visibility = Visibility.Collapsed;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            timer.Tick += (_, __) => { timer.Stop(); Close(); };
            timer.Start();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();

        // Reset all settings to factory defaults
        private void ResetDefaultsBtn_Click(object sender, RoutedEventArgs e)
        {
            var answer = System.Windows.MessageBox.Show(
                "This will reset ALL settings to factory defaults and restart the dialog.\n\nYour hotkey bindings and presets will also be cleared.\n\nContinue?",
                "Reset to Defaults",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            if (answer != System.Windows.MessageBoxResult.Yes) return;

            var defaults = new AppSettings();
            defaults.Save();
            _applyCallback?.Invoke(defaults);

            StatusLabel.Foreground = (System.Windows.Media.Brush)
                Application.Current.Resources["BrushGreenText"];
            StatusLabel.Text = "Reset to defaults!";
            StatusIcon.Visibility = Visibility.Collapsed;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            timer.Tick += (_, __) => { timer.Stop(); Close(); };
            timer.Start();
        }

        // ── Inline validation (WCAG 3.3.1 Error Identification) ─────────────
        // Guards are required: TextChanged fires during XAML load (when default
        // Text values are applied) before InitializeComponent returns, so the
        // error TextBlocks and SaveBtn are still null at that point.
        private void LensWBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (LensWError == null) return;
            bool ok = int.TryParse(LensWBox.Text.Trim(), out int v) && v >= 200 && v <= 3840;
            LensWError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            UpdateSaveBtn();
        }

        private void LensHBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (LensHError == null) return;
            bool ok = int.TryParse(LensHBox.Text.Trim(), out int v) && v >= 150 && v <= 2160;
            LensHError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            UpdateSaveBtn();
        }

        private void ZoomBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (ZoomError == null) return;
            // Accept both locale (2,00) and invariant (2.00) decimal separators
            bool ok = (double.TryParse(ZoomBox.Text.Trim(),
                           System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.CurrentCulture, out double v) ||
                       double.TryParse(ZoomBox.Text.Trim(),
                           System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out v))
                      && v >= 1.25 && v <= 20;
            ZoomError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            UpdateSaveBtn();
        }

        private void UpdateSaveBtn()
        {
            if (SaveBtn == null) return;
            bool valid = LensWError.Visibility  == Visibility.Collapsed
                      && LensHError.Visibility  == Visibility.Collapsed
                      && ZoomError.Visibility   == Visibility.Collapsed;
            SaveBtn.IsEnabled = valid;
            SaveBtn.ToolTip   = valid ? "Save settings" : "Fix errors above before saving";
        }

        private bool TryParseInputs(out int w, out int h, out double zoom)
        {
            w = 0; h = 0; zoom = 0;
            if (!int.TryParse(LensWBox.Text.Trim(), out w) || w < 200 || w > 3840) return false;
            if (!int.TryParse(LensHBox.Text.Trim(), out h) || h < 150 || h > 2160) return false;
            // Accept both locale (2,00) and invariant (2.00) decimal separators
            if (!double.TryParse(ZoomBox.Text.Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.CurrentCulture, out zoom) &&
                !double.TryParse(ZoomBox.Text.Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out zoom))
                return false;
            if (zoom < 1.25 || zoom > 20) return false;
            return true;
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("SMLVSettingsDialog", 620, 600);
            SizeToContent = System.Windows.SizeToContent.Manual;
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("SMLVSettingsDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
