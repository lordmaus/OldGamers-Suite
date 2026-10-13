using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BadgeOverlay
{
    /// <summary>
    /// Free-floating large-text info panel for low-vision users.
    /// Drag the title bar to reposition anywhere on screen.
    /// A+ / A- buttons resize font (WCAG 1.4.4).
    /// Always free-floating — no pin/attach mode.
    ///
    /// Keyboard accessibility (WCAG 2.1.1 — Keyboard):
    ///   • Arrow keys (when window focused): move by 20 px
    ///   • Shift + Arrow: move by 1 px (fine)
    ///   • Ctrl + Arrow: move by 100 px (coarse)
    ///   • Ctrl + Home: centre on primary screen
    ///   • Escape: close/hide
    ///   • + / = : increase font;  - / _ : decrease font
    ///   • Alt+Arrow (from any child control): move 10 px, Alt+Shift+Arrow 1 px
    ///   • Ctrl+Alt+Arrow: resize; title-bar context menu (Shift+F10): Move/Resize with keyboard
    /// </summary>
    public partial class InfoOverlay : Window
    {
        private DetectedElement _lastDetected;
        private AppSettings    _s;

        // Cached frozen brushes — reused across UpdateContent calls.
        // Only recreated when the element's color actually changes.
        private SolidColorBrush _cachedBorderBrush;
        private SolidColorBrush _cachedBadgeBrush;
        private Color           _cachedBorderColor;
        private Color           _cachedBadgeColor;
        private int             _cachedPaletteVersion = -1;

        private const double FontMin = 12;
        private const double FontMax = 72;
        private double _fontSize = 24;

        public InfoOverlay()
        {
            InitializeComponent();
            CursorPreserver.Attach(this);
            Loaded           += OnLoaded;
            KeyDown          += OnKeyDown;
            LocationChanged  += OnLocationChanged;
            // Allow the window itself to receive focus so arrow-key drag works
            // without the user having to tab to a child control first.
            Focusable = true;
            // WCAG 2.1.3 — the DragBar move is mouse-only; Alt+Arrow works even when a
            // title-bar button has focus (plain arrows only reach OnKeyDown on the window).
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, DragBar, allowResize: true);
        }

        /// <summary>
        /// Binds this overlay to an AppSettings instance. Initial font size
        /// and position are restored from settings; subsequent user changes
        /// are written back and persisted via the main save pipeline.
        /// </summary>
        public void BindSettings(AppSettings s)
        {
            _s = s;
            if (s != null && s.InfoOverlayFontSize >= FontMin && s.InfoOverlayFontSize <= FontMax)
                _fontSize = s.InfoOverlayFontSize;
            ApplyFontSize(_fontSize);
            RestorePosition();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_s == null) ApplyFontSize(_fontSize);
        }

        private void RestorePosition()
        {
            if (_s == null) return;
            // Sentinel = -1 ("not yet set") — also tolerate NaN from
            // settings.json files written by earlier builds. Both signal
            // "no saved position; let WindowStartupLocation centre us".
            if (_s.InfoOverlayLeft < 0 || _s.InfoOverlayTop < 0
                || double.IsNaN(_s.InfoOverlayLeft) || double.IsNaN(_s.InfoOverlayTop))
                return;
            // Clamp to virtual screen — monitor topology may have changed.
            double minX = SystemParameters.VirtualScreenLeft;
            double minY = SystemParameters.VirtualScreenTop;
            double maxX = minX + SystemParameters.VirtualScreenWidth  - 80;
            double maxY = minY + SystemParameters.VirtualScreenHeight - 40;
            Left = Math.Max(minX, Math.Min(_s.InfoOverlayLeft, maxX));
            Top  = Math.Max(minY, Math.Min(_s.InfoOverlayTop,  maxY));
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            if (_s == null) return;
            _s.InfoOverlayLeft = Left;
            _s.InfoOverlayTop  = Top;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Re-renders the last detected element (if any) so a settings
        /// toggle that affects display — like ShowInfoOverlayMeta — takes
        /// effect immediately, without waiting for cursor motion.
        /// </summary>
        public void RefreshLast()
        {
            if (_lastDetected == null) return;
            var snapshot = _lastDetected;
            _lastDetected = null;        // bypass the no-op equality guard
            UpdateContent(snapshot);
        }

        /// <summary>BO-02: show/hide the "can't read this app" strip.</summary>
        public void SetDetectionDegraded(bool degraded)
        {
            DegradedText.Text         = degraded ? OverlayWindow.DetectionDegradedText : "";
            DegradedBanner.Visibility = degraded ? Visibility.Visible : Visibility.Collapsed;
            if (degraded && IsVisible && Height < 240) Height = 240;   // keep the label row visible
        }

        public void UpdateContent(DetectedElement btn)
        {
            if (btn == null)
            {
                if (_lastDetected == null) return;
                _lastDetected = null;
                InfoLabel.Text    = "No button detected";
                InfoCategory.Text = "Hover a recognised button";
                InfoIcon.Text     = "◉";
                MainBorder.SetResourceReference(Border.BorderBrushProperty, OverlayPalette.Border);
                InfoLabel.SetResourceReference(TextBlock.ForegroundProperty, OverlayPalette.Text);
                InfoIcon.SetResourceReference(TextBlock.ForegroundProperty, OverlayPalette.Text);
                IconPillBg.Color       = Color.FromArgb(30, 128, 128, 128);
                DragBarBrush.Color     = OverlayPalette.BarColor;
                InfoMeta.Visibility    = Visibility.Collapsed;
                return;
            }

            if (_lastDetected != null && _lastDetected.Label == btn.Label) return;
            _lastDetected = btn;

            InfoLabel.Text    = btn.Label;
            InfoCategory.Text = btn.Category;
            InfoIcon.Text     = btn.Icon;

            bool paletteChanged = _cachedPaletteVersion != OverlayPalette.Version;
            _cachedPaletteVersion = OverlayPalette.Version;
            if (paletteChanged || _cachedBorderBrush == null || _cachedBorderColor != btn.BorderColor)
            {
                _cachedBorderColor  = btn.BorderColor;
                _cachedBorderBrush  = new SolidColorBrush(OverlayPalette.BorderColor(btn.BorderColor));
                _cachedBorderBrush.Freeze();
            }
            if (paletteChanged || _cachedBadgeBrush == null || _cachedBadgeColor != btn.BadgeColor)
            {
                // Category colour, or OverlayText when < 4.5:1 on the overlay bg / HC active (BO-05).
                _cachedBadgeColor = btn.BadgeColor;
                _cachedBadgeBrush = OverlayPalette.LabelBrush(btn.BadgeColor);
            }
            MainBorder.BorderBrush = _cachedBorderBrush;
            InfoLabel.Foreground   = _cachedBadgeBrush;
            InfoIcon.Foreground    = _cachedBadgeBrush;
            IconPillBg.Color = Color.FromArgb(50,
                btn.BadgeColor.R, btn.BadgeColor.G, btn.BadgeColor.B);
            DragBarBrush.Color = OverlayPalette.IsHighContrast
                ? OverlayPalette.BarColor
                : Color.FromArgb(35, btn.BadgeColor.R, btn.BadgeColor.G, btn.BadgeColor.B);

            // ── Extended metadata ─────────────────────────────────────────────
            // Each line appears only when the underlying field is populated.
            // ── Extended metadata ─────────────────────────────────────────────
            // Skipped entirely when the user opts out. Keeps the panel
            // minimal for users who find the dense detail overwhelming —
            // accessibility-as-progressive-disclosure.
            if (_s != null && !_s.ShowInfoOverlayMeta)
            {
                InfoMeta.Visibility = Visibility.Collapsed;
                return;
            }

            // Priority-ranked list. We show AT MOST 3 lines to keep the
            // panel compact and readable at any font size. Process name /
            // path and verbose description are intentionally omitted from
            // the always-shown stack — the label + category already
            // answer "what is this", and more detail would clutter.
            var lines = new System.Collections.Generic.List<string>(3);

            void Add(string line)
            {
                if (lines.Count < 3 && !string.IsNullOrEmpty(line))
                    lines.Add(line);
            }

            if (!string.IsNullOrEmpty(btn.AcceleratorKey))
                Add($"⌨  {btn.AcceleratorKey}");
            if (btn.IsRequired)
                Add("*  Required");
            if (!string.IsNullOrEmpty(btn.ToggleState))
                Add($"●  {btn.ToggleState}");
            if (!string.IsNullOrEmpty(btn.SelectionState))
                Add($"✓  {btn.SelectionState}");
            if (!string.IsNullOrEmpty(btn.ValueText))
                Add($"⇅  {btn.ValueText}");
            if (!string.IsNullOrEmpty(btn.ScrollPercent))
                Add($"▤  {btn.ScrollPercent}");
            if (!string.IsNullOrEmpty(btn.TabUrl))
                Add($"🌐  {Truncate(btn.TabUrl, 50)}");
            else if (!string.IsNullOrEmpty(btn.LinkUrl) && btn.LinkUrl != btn.Label)
                Add($"🔗  {Truncate(btn.LinkUrl, 50)}");
            if (!string.IsNullOrEmpty(btn.ItemType))
                Add($"📄  {btn.ItemType}");

            string metaText = string.Join("\n", lines);

            if (metaText.Length == 0)
            {
                InfoMeta.Visibility = Visibility.Collapsed;
            }
            else
            {
                InfoMeta.Text       = metaText;
                InfoMeta.Visibility = Visibility.Visible;
            }
        }

        // ── Font size ─────────────────────────────────────────────────────────

        private void BtnFontInc_Click(object sender, RoutedEventArgs e)
            => ApplyFontSize(Math.Min(FontMax, _fontSize + 4));

        private void BtnFontDec_Click(object sender, RoutedEventArgs e)
            => ApplyFontSize(Math.Max(FontMin, _fontSize - 4));

        private void ApplyFontSize(double size)
        {
            _fontSize             = size;
            // WCAG 1.4.4 — honour the OS text-scale factor so users who
            // set Windows "Make text bigger" to 150 % see this overlay
            // scale in step. The slider value is the user's base size;
            // we apply the OS multiplier on top so the same slider
            // setting produces a larger rendering for high-DPI / text-
            // scaled users. Falls back to 1.0 if the API isn't available.
            double osScale = GetOsTextScale();
            double effective = size * osScale;

            InfoLabel.FontSize    = effective;
            InfoIcon.FontSize     = effective;
            InfoCategory.FontSize = Math.Max(OldGamers.Wpf.Shared.Controls.TypeRamp.Caption, Math.Round(effective * 0.55));
            FontSizeLabel.Text    = osScale > 1.01
                ? $"{(int)size} pt  (×{osScale:F2})"
                : $"{(int)size} pt";
            if (_s != null) _s.InfoOverlayFontSize = size;
        }

        /// <summary>
        /// Reads the Windows "Make text bigger" accessibility setting.
        /// Located at HKCU\Software\Microsoft\Accessibility\TextScaleFactor
        /// where 100 = 1.0× (default), 225 = 2.25× (maximum).
        /// Returns 1.0 if the key is absent or unreadable.
        /// </summary>
        private static double GetOsTextScale()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(@"Software\Microsoft\Accessibility", writable: false);
                if (key?.GetValue("TextScaleFactor") is int pct && pct >= 100)
                    return Math.Min(2.25, pct / 100.0);
            }
            catch { }
            return 1.0;
        }

        // ── Drag ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Truncates <paramref name="s"/> to at most <paramref name="max"/>
        /// characters, appending an ellipsis when shortened. Returns the
        /// original string when already within bounds. Used for URL display
        /// where a too-wide line would push the panel into next-line wrap
        /// territory and break the visual rhythm.
        /// </summary>
        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max - 3) + "…";
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        /// <summary>
        /// Bottom-right corner resize. Applies DragDelta directly to
        /// Width/Height with MinWidth/MinHeight from the Window (enforced
        /// by WPF). Uses Math.Max so a fast upward drag doesn't collapse
        /// the window to zero.
        /// </summary>
        private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            double newW = Width  + e.HorizontalChange;
            double newH = Height + e.VerticalChange;
            if (newW >= MinWidth)  Width  = newW;
            if (newH >= MinHeight) Height = newH;
        }

        // ── Keyboard drag (WCAG 2.1.1) ────────────────────────────────────────

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Escape closes / hides
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }

            // + / - font resize
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                ApplyFontSize(Math.Min(FontMax, _fontSize + 4));
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                ApplyFontSize(Math.Max(FontMin, _fontSize - 4));
                e.Handled = true;
                return;
            }

            // Ctrl+Home: centre on primary work area
            if (e.Key == Key.Home && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + (wa.Width  - ActualWidth)  / 2;
                Top  = wa.Top  + (wa.Height - ActualHeight) / 2;
                ClampToScreen();
                e.Handled = true;
                return;
            }

            // Arrow-key drag — step sizes scale with modifier.
            // Shift = 1 px (precision), plain = 20 px, Ctrl = 100 px (coarse).
            double step = 20;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   step = 1;
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) step = 100;

            switch (e.Key)
            {
                case Key.Left:  Left -= step; ClampToScreen(); e.Handled = true; break;
                case Key.Right: Left += step; ClampToScreen(); e.Handled = true; break;
                case Key.Up:    Top  -= step; ClampToScreen(); e.Handled = true; break;
                case Key.Down:  Top  += step; ClampToScreen(); e.Handled = true; break;
            }
        }

        /// <summary>
        /// Keeps the window inside the virtual screen bounds so keyboard
        /// drag can't move it off-screen where a user might lose it.
        /// </summary>
        private void ClampToScreen()
        {
            double minX = SystemParameters.VirtualScreenLeft;
            double minY = SystemParameters.VirtualScreenTop;
            double maxX = minX + SystemParameters.VirtualScreenWidth  - Math.Max(ActualWidth,  80);
            double maxY = minY + SystemParameters.VirtualScreenHeight - Math.Max(ActualHeight, 40);
            if (Left < minX) Left = minX;
            if (Top  < minY) Top  = minY;
            if (Left > maxX) Left = maxX;
            if (Top  > maxY) Top  = maxY;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("BadgeInfoOverlay", 360, 180);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("BadgeInfoOverlay", Width, Height);
            base.OnClosed(e);
        }
    }
}