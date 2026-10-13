using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static SuperMagnifier.NativeMethods;

namespace SuperMagnifier
{
    public partial class LensWindow : Window
    {
        // ── Cached frozen brushes (feedback states — not theme-dependent) ─────
        private static readonly SolidColorBrush BrushSuccess  = Freeze(Color.FromRgb(166, 227, 161));
        private static readonly SolidColorBrush BrushInactive = Freeze(Color.FromRgb(166, 173, 200));
        private static readonly SolidColorBrush BrushActive   = Freeze(Color.FromRgb(137, 180, 250));
        private static SolidColorBrush Freeze(Color c)
        { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // ── P/Invoke — see NativeMethods.cs ─────────────────────────────────────

        // ── Fields ────────────────────────────────────────────────────────────
        private HwndSource  _hwndSource;
        private MagHwndHost _magHost;
        private bool        _frozen       = false;
        private bool        _clickThrough = false;
        private bool        _colourPick   = false;
        private int         _srcLeft, _srcTop, _srcRight, _srcBottom;

        // Title bar size cycling
        public static readonly int[]    TitleHeights    = { 26, 36, 48, 64, 80 };
        private static readonly double[] TitleLabelFonts = { 13, 14, 18, 22, 26 };   // smallest 13 → toolbar fs-1 = 12px floor
        public  static readonly string[] TitleSizeNames  = { "Normal", "Large", "X-Large", "XX-Large", "XXX-Large" };
        private int _titleSizeIndex = 4;   // default: XXX-Large
        public  int TitleBarSizeIndex => _titleSizeIndex;
        public  double TitleBarActualHeight => TitleHeights[_titleSizeIndex];

        // ── Public surface ────────────────────────────────────────────────────
        public IntPtr MagHwnd    => _magHost?.MagHwnd ?? IntPtr.Zero;
        // ViewWidth/ViewHeight in logical WPF pixels — passed to UpdateSource as a
        // fallback size hint. UpdateSource uses GetClientRect(magHwnd) for the actual
        // physical pixel size; these values are the fallback when GetClientRect fails.
        public int    ViewWidth  => Math.Max(4, (int)Math.Round(MagHostBorder.ActualWidth));
        public int    ViewHeight => Math.Max(4, (int)Math.Round(MagHostBorder.ActualHeight));

        /// <summary>
        /// Returns the position and size of the magnifier view area in device pixels
        /// relative to the lens window's own client origin — for child-window placement.
        /// </summary>
        public (int x, int y, int w, int h) GetViewDeviceRect()
        {
            try
            {
                // Get screen position of MagHostBorder and the lens window's own (0,0)
                // PointToScreen on the window itself gives the client-area origin in device px
                var lensOrigin = this.PointToScreen(new Point(0, 0));
                var viewOrigin = MagHostBorder.PointToScreen(new Point(0, 0));

                int x = (int)Math.Round(viewOrigin.X - lensOrigin.X);
                int y = (int)Math.Round(viewOrigin.Y - lensOrigin.Y);
                int w = (int)Math.Round(MagHostBorder.ActualWidth  * DpiScaleX);
                int h = (int)Math.Round(MagHostBorder.ActualHeight * DpiScaleY);
                return (x, y, w, h);
            }
            catch { return (0, 0, 100, 100); }
        }

        /// <summary>
        /// Returns the absolute screen coordinates (device px) of the magnifier
        /// view area — used to position the reticle overlay window over it.
        /// </summary>
        public (int x, int y, int w, int h) GetViewScreenRect()
        {
            try
            {
                var viewOrigin = MagHostBorder.PointToScreen(new Point(0, 0));
                int x = (int)Math.Round(viewOrigin.X);
                int y = (int)Math.Round(viewOrigin.Y);
                int w = (int)Math.Round(MagHostBorder.ActualWidth  * DpiScaleX);
                int h = (int)Math.Round(MagHostBorder.ActualHeight * DpiScaleY);
                return (x, y, w, h);
            }
            catch { return (0, 0, 0, 0); }
        }
        public bool   Frozen             => _frozen;
        public bool   IsClickThrough     => _clickThrough;
        public bool   IsColourPickActive => _colourPick;
        public void   ToggleColourPick() => ColourPickBtn_Click(null, null);
        public float  DpiScaleX  { get; private set; } = 1f;
        public float  DpiScaleY  { get; private set; } = 1f;

        public event EventHandler MagReady;
        public event EventHandler ViewSizeChanged;
        public event EventHandler FreezeToggled;
        public event EventHandler CloseRequested;
        // Raised after colour pick so MainWindow can show the hex/RGB toast
        public event EventHandler<string> ColourPicked;

        public void SetSourceRect(int l, int t, int r, int b)
            => (_srcLeft, _srcTop, _srcRight, _srcBottom) = (l, t, r, b);

        // ── Init ──────────────────────────────────────────────────────────────
        public LensWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource == null) return;

            IntPtr hwnd = _hwndSource.Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            if (_hwndSource.CompositionTarget != null)
            {
                var m = _hwndSource.CompositionTarget.TransformToDevice;
                DpiScaleX = (float)m.M11;
                DpiScaleY = (float)m.M22;
            }

            int one = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref one, sizeof(int));
            _hwndSource.AddHook(WndProc);
            EnsureMagHost();
        }

        private void EnsureMagHost()
        {
            if (_magHost != null) return;
            // HwndHost child windows are sized in the WPF layout's logical pixel space.
            // WPF's HwndHost infrastructure handles DPI mapping internally.
            int w = Math.Max(4, (int)Math.Round(MagHostBorder.ActualWidth));
            int h = Math.Max(4, (int)Math.Round(MagHostBorder.ActualHeight));
            if (w < 4 || h < 4) return;
            _magHost = new MagHwndHost(w, h);
            MagHostBorder.Child = _magHost;
            MagReady?.Invoke(this, EventArgs.Empty);
        }

        private void MagHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_magHost == null) { EnsureMagHost(); return; }
            int w = Math.Max(4, (int)Math.Round(MagHostBorder.ActualWidth));
            int h = Math.Max(4, (int)Math.Round(MagHostBorder.ActualHeight));
            _magHost.ResizeMag(w, h);
            ViewSizeChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Keyboard: Escape still supported for any future panel ─────────────
        private void LensWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Alt+H kept for backward compatibility — no-op now bar is always visible
            if (e.Key == Key.H &&
                (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                HoverBar.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                e.Handled = true;
            }
        }


        // ── Snapshot ──────────────────────────────────────────────────────────
        private void SnapBtn_Click(object sender, RoutedEventArgs e) => SaveSnapshot();

        public void SaveSnapshot()
        {
            try
            {
                int w = Math.Max(1, _srcRight  - _srcLeft);
                int h = Math.Max(1, _srcBottom - _srcTop);

                // SM-07: every GDI handle released in finally — previously leaked on any exception.
                IntPtr screenDC = IntPtr.Zero, memDC = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
                System.Drawing.Image captured;
                try
                {
                    screenDC = GetDC(IntPtr.Zero);
                    memDC    = CreateCompatibleDC(screenDC);
                    bmp      = CreateCompatibleBitmap(screenDC, w, h);
                    old      = SelectObject(memDC, bmp);
                    BitBlt(memDC, 0, 0, w, h, screenDC, _srcLeft, _srcTop, SRCCOPY);
                    if (old != IntPtr.Zero) { SelectObject(memDC, old); old = IntPtr.Zero; }
                    captured = System.Drawing.Image.FromHbitmap(bmp);
                }
                finally
                {
                    if (old      != IntPtr.Zero) SelectObject(memDC, old);
                    if (memDC    != IntPtr.Zero) DeleteDC(memDC);
                    if (screenDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDC);
                    if (bmp      != IntPtr.Zero) DeleteObject(bmp);
                }

                using (var img = captured)
                {
                    string folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "Super Magnifier LV");
                    Directory.CreateDirectory(folder);
                    // PNG: lossless, ideal for screen and text content
                    string path = Path.Combine(folder,
                        $"Snap_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
                    img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                SnapBtn.Foreground = BrushSuccess;
                var t = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(600) };
                t.Tick += (s2, e2) => { t.Stop();
                    SnapBtn.Foreground = BrushInactive; };
                t.Start();
            }
            catch (System.Exception _ex) { CrashLog.Write("SaveSnapshot", _ex); }
        }

        // ── Colour picker ─────────────────────────────────────────────────────
        private void ColourPickBtn_Click(object sender, RoutedEventArgs e)
        {
            _colourPick = !_colourPick;
            ColourPickBtn.Foreground = _colourPick
                ? BrushSuccess
                : BrushInactive;
            ColourPickBtn.Tag = _colourPick
                ? "Colour picker ON — click pixel to copy hex"
                : "Colour picker — click pixel to copy hex";
            MagHostBorder.Cursor = _colourPick
                ? System.Windows.Input.Cursors.Cross
                : null;
            // Show or hide the colour history bar when entering/leaving pick mode
            ColourHistoryBar.Visibility = _colourPick && ColourSwatchPanel.Children.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Rebuild colour history swatches from the saved hex list.</summary>
        public void LoadColourHistory(System.Collections.Generic.List<string> history)
        {
            ColourSwatchPanel.Children.Clear();
            if (history == null || history.Count == 0) return;
            var hexes = history;
            foreach (var hex in hexes)
            {
                if (string.IsNullOrWhiteSpace(hex)) continue;
                if (!ColourPicker.TryParseColor(hex.Trim(), out var col)) continue;
                var btn = new Button
                {
                    Width   = 20, Height = 20,
                    Margin  = new Thickness(2, 0, 2, 0),
                    Background  = new SolidColorBrush(col),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    ToolTip = hex.Trim(),
                    Cursor  = System.Windows.Input.Cursors.Hand,
                    Style   = null,
                    Tag     = hex.Trim()
                };
                System.Windows.Automation.AutomationProperties.SetName(btn, $"Re-copy {hex.Trim()}");
                btn.Click += (s, e) =>
                {
                    try { Clipboard.SetText((string)((Button)s).Tag); } catch { }
                    ColourPicked?.Invoke(this, (string)((Button)s).Tag);
                };
                ColourSwatchPanel.Children.Add(btn);
            }
        }

        private void MagHost_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_colourPick)
            {
                // Drag the whole lens window when clicking the view area
                DragMove();
                return;
            }
            string hex = ColourPicker.PickAndCopy();
            if (hex == null)
            {
                // Clipboard may be locked by another app — surface a clear failure toast
                ColourPicked?.Invoke(this, "⚠ Colour pick failed — clipboard may be in use");
                return;
            }

            // Build hex+RGB description and fire event so MainWindow can toast it
            string toastText = hex;
            if (ColourPicker.TryParseColor(hex, out var c))
            {
                ColourPickBtn.Foreground = new SolidColorBrush(c);
                toastText = $"{hex}   rgb({c.R}, {c.G}, {c.B})";
            }

            ColourPicked?.Invoke(this, toastText);

            var t = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(800) };
            t.Tick += (s2, e2) =>
            {
                t.Stop();
                ColourPickBtn.Foreground = BrushSuccess;
            };
            t.Start();
        }

        // ── Click-through ─────────────────────────────────────────────────────
        private void ClickThruBtn_Click(object sender, RoutedEventArgs e)
            => SetClickThrough(!_clickThrough);

        public void SetClickThrough(bool on)
        {
            _clickThrough = on;

            if (_hwndSource == null) return;
            IntPtr hwnd = _hwndSource.Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (on)
            {
                // WS_EX_TRANSPARENT alone is not enough — WS_EX_LAYERED must also
                // be present for click-through to work. WPF's AllowsTransparency sets
                // it, but we re-read and re-apply here to be certain.
                SetWindowLong(hwnd, GWL_EXSTYLE,
                    ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            }
            else
            {
                // Remove only WS_EX_TRANSPARENT — keep WS_EX_LAYERED so WPF
                // transparency continues to work correctly.
                SetWindowLong(hwnd, GWL_EXSTYLE,
                    ex & ~WS_EX_TRANSPARENT);
            }

            ClickThruBtn.Foreground = on
                ? BrushSuccess
                : BrushInactive;
            // Dim the toolbar visually so the user knows click-through is active.
            // Opacity alone does NOT control hit-testing — that is done via ExStyle above.
            HoverBar.Opacity = on ? 0.45 : 1.0;
            ClickThruBtn.Tag = on
                ? "Click-through ON — click to disable"
                : "Click-through — mouse passes through lens";
        }

        // ── Title bar size ────────────────────────────────────────────────────
        private void TitleSizeBtn_Click(object sender, RoutedEventArgs e)
            => SetTitleBarSize((_titleSizeIndex + 1) % TitleHeights.Length);

        public void SetTitleBarSize(int index)
        {
            _titleSizeIndex = Math.Max(0, Math.Min(TitleHeights.Length - 1, index));
            int    h  = TitleHeights[_titleSizeIndex];
            double fs = TitleLabelFonts[_titleSizeIndex];
            TitleRow.Height = new GridLength(h);

            // Scale button vertical padding so content never overflows the row.
            // At h=26 buttons get 0 vertical padding; at h=80 they get 6px each side.
            double vpad = h <= 26 ? 0 : h <= 36 ? 1 : h <= 48 ? 2 : h <= 64 ? 4 : 6;

            void ScaleBtn(System.Windows.Controls.Button b, double fontSize, double? extraPad = null)
            {
                if (b == null) return;
                b.FontSize = fontSize;
                b.Padding  = new System.Windows.Thickness(
                    b.Padding.Left, (extraPad ?? vpad),
                    b.Padding.Right, (extraPad ?? vpad));
            }

            // Scale all toolbar text and padding to match the new height
            if (FreezeBtnText   != null) FreezeBtnText.FontSize   = fs;
            if (LensZoomLabel   != null) LensZoomLabel.FontSize   = fs;

            ScaleBtn(LensZoomInBtn,   fs + 4);
            ScaleBtn(LensZoomOutBtn,  fs + 4);
            ScaleBtn(FreezeBtn,       fs);
            ScaleBtn(ColourPickBtn,   fs - 1);
            ScaleBtn(SnapBtn,         fs - 1);
            ScaleBtn(ClickThruBtn,    fs - 1);
            ScaleBtn(CloseBtn,        fs);

            if (TitleSizeBtn != null)
            {
                TitleSizeBtn.FontSize = fs - 1;
                TitleSizeBtn.Padding  = new System.Windows.Thickness(
                    TitleSizeBtn.Padding.Left, vpad,
                    TitleSizeBtn.Padding.Right, vpad);
                if (TitleSizeBtn.Content is System.Windows.Controls.TextBlock tb)
                    tb.FontSize = fs - 1;
            }

            // Dock arrow buttons — scale padding only, font stays fixed
            foreach (var btn in new System.Windows.Controls.Button[]
                { DockTLBtn, DockTRBtn, DockBLBtn, DockBRBtn })
            {
                if (btn != null)
                    btn.Padding = new System.Windows.Thickness(
                        btn.Padding.Left, vpad,
                        btn.Padding.Right, vpad);
            }

            // Keep window MinHeight tall enough to always show title bar + some magnifier area
            MinHeight = h + 60;

            // Scale the visual separator lines between button groups
            double sepH = Math.Max(8, h * 0.55);
            if (ToolbarSep1 != null) ToolbarSep1.Height = sepH;
            if (ToolbarSep2 != null) ToolbarSep2.Height = sepH;

            TitleSizeBtn.Tag = $"Title bar: {TitleSizeNames[_titleSizeIndex]} — click to cycle";
        }


        // ── Zoom controls on lens toolbar ────────────────────────────────────
        // MainWindow wires these events to its own StepZoom method.
        public event EventHandler ZoomInRequested;
        public event EventHandler ZoomOutRequested;
        public event EventHandler SizeCycleRequested;

        private void LensZoomInBtn_Click(object sender, RoutedEventArgs e)
            => ZoomInRequested?.Invoke(this, EventArgs.Empty);

        private void LensZoomOutBtn_Click(object sender, RoutedEventArgs e)
            => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

        private void SizeCycleBtn_Click(object sender, RoutedEventArgs e)
            => SizeCycleRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Called by MainWindow whenever zoom changes to keep label in sync.</summary>
        public void UpdateZoomLabel(double zoom)
        {
            if (LensZoomLabel != null)
                LensZoomLabel.Text = $"{zoom:F2}x";
        }

        // ── Lens bar text visibility (Settings → individually hide bar labels) ──
        private bool _barHideAllText, _barShowSize = true, _barShowColour = true,
                     _barShowSnap = true, _barShowClickThru = true, _barShowFreeze = true,
                     _barShowZoomValue = true, _barShowHoverHint = true;

        /// <summary>
        /// Applies per-element text visibility on the lens top bar. Icon-only controls
        /// (dock arrows, Close ✕) are never affected. AutomationProperties.Name stays
        /// set on every button regardless, so screen readers keep working when text
        /// is hidden visually.
        /// </summary>
        public void SetBarTextVisibility(bool hideAll, bool showSize, bool showColour,
            bool showSnap, bool showClickThru, bool showFreeze, bool showZoomValue, bool showHoverHint)
        {
            _barHideAllText    = hideAll;
            _barShowSize       = showSize;
            _barShowColour     = showColour;
            _barShowSnap       = showSnap;
            _barShowClickThru  = showClickThru;
            _barShowFreeze     = showFreeze;
            _barShowZoomValue  = showZoomValue;
            _barShowHoverHint  = showHoverHint;

            bool Eff(bool individual) => !_barHideAllText && individual;

            if (TitleSizeBtnText != null) TitleSizeBtnText.Text = Eff(_barShowSize) ? "Aa" : "";
            if (ColourPickBtn    != null) ColourPickBtn.Content = Eff(_barShowColour) ? "Clr" : "";
            if (SnapBtn          != null) SnapBtn.Content       = Eff(_barShowSnap) ? "Snap" : "";
            if (ClickThruBtn     != null) ClickThruBtn.Content  = Eff(_barShowClickThru) ? "Thru" : "";

            if (LensZoomLabel != null)
                LensZoomLabel.Visibility = Eff(_barShowZoomValue) ? Visibility.Visible : Visibility.Collapsed;

            // Freeze button keeps its snowflake/play icon either way — only the word toggles.
            RefreshFreezeButtonText();

            if (!Eff(_barShowHoverHint) && HintLabel != null)
                HintLabel.Text = "";
        }

        // ── Inline hint label (replaces tooltips — no popup HWND) ────────────
        /// <summary>Hover hints show the live hotkeys (SM-12).</summary>
        public void SetHotkeyHints(string freeze, string zoomIn, string zoomOut)
        {
            FreezeBtn.Tag      = $"Freeze / unfreeze lens ({freeze})";
            LensZoomInBtn.Tag  = $"Zoom in ({zoomIn})";
            LensZoomOutBtn.Tag = $"Zoom out ({zoomOut})";
        }

        private void Btn_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_barHideAllText && _barShowHoverHint
                && sender is System.Windows.FrameworkElement fe && HintLabel != null)
                HintLabel.Text = fe.Tag as string ?? "";
        }

        private void Btn_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (HintLabel != null) HintLabel.Text = "";
        }

        // ── Freeze ────────────────────────────────────────────────────────────
        private void FreezeBtn_Click(object sender, RoutedEventArgs e) => ToggleFreeze();

        public void ToggleFreeze()
        {
            _frozen = !_frozen;
            RefreshFreezeButtonText();
            FreezeBtnText.Foreground = _frozen
                ? BrushActive
                : BrushInactive;
            FreezeToggled?.Invoke(this, EventArgs.Empty);
        }

        // Whole Freeze button (icon + word) respects the bar-text settings —
        // unchecking "Freeze / Resume word" hides the entire button, not just the text.
        private void RefreshFreezeButtonText()
        {
            if (FreezeBtnText == null) return;
            bool show = !_barHideAllText && _barShowFreeze;
            string icon = _frozen ? "▶" : "❄";
            FreezeBtnText.Text = show ? $"{icon} {(_frozen ? "Resume" : "Freeze")}" : icon;
            if (FreezeBtn != null)
                FreezeBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Appearance ────────────────────────────────────────────────────────
        public void SetBorderColor(string hexColor)
        {
            if (ColourPicker.TryParseColor(hexColor, out var col))
                OuterBorder.BorderBrush = new SolidColorBrush(col);
        }

        // Cached so SetTrackMode's Fixed branch can restore the user's actual
        // border/grip settings instead of hardcoded defaults (see below).
        private int    _desiredBorderThickness = 2;
        private double _desiredGripSize        = 16;

        public void SetBorderThickness(int px)
        {
            int t = Math.Max(1, Math.Min(10, px));
            _desiredBorderThickness = t;
            OuterBorder.BorderThickness = new Thickness(t);
            ForceLayeredRedraw();
        }

        // AllowsTransparency layered windows don't reliably repaint their composited
        // surface when only a layout-affecting property (BorderThickness, a
        // RowDefinition.Height) changes — the new value applies in the object model
        // right away, but the pixels on screen stay stale until a genuine WM_MOVE/
        // WM_SIZE occurs (this is exactly why dragging the lens "fixes" it). A
        // SWP_FRAMECHANGED SetWindowPos no-op nudge forces that repaint without
        // actually moving or resizing the window.
        private void ForceLayeredRedraw()
        {
            UpdateLayout();
            if (_hwndSource == null) return; // not shown yet — first real paint will be correct
            NativeMethods.SetWindowPos(_hwndSource.Handle, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }

        public void SetLensOpacity(double opacity)
            => Opacity = Math.Max(0.2, Math.Min(1.0, opacity));

        // Resize-grip colour/size — low-vision affordance for the bottom drag handles.
        public void SetResizeGripAppearance(string hexColor, double size)
        {
            double t = Math.Max(44, Math.Min(64, size));   // WCAG 2.5.5: ≥44px target (was 8–32)
            _desiredGripSize = t;
            ResizeRow.Height = new GridLength(t);

            Color c = ColourPicker.ParseColorOrDefault(hexColor, Color.FromRgb(0x89, 0xB4, 0xFA));
            var fill = new SolidColorBrush(c);
            ResizeGripS.Fill  = fill;
            ResizeGripSW.Fill = fill;
            ResizeGripSE.Fill = fill;

            // Contrast-aware glyph colour (black/white) to keep grip dots legible on any grip colour.
            double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            var glyph = new SolidColorBrush(luminance > 140 ? Colors.Black : Colors.White);
            ResizeGripDotsS.Foreground  = glyph;
            ResizeGripDotsSE.Foreground = glyph;
            ResizeGripDotsS.FontSize  = Math.Max(7, t * 0.45);
            ResizeGripDotsSE.FontSize = Math.Max(8, t * 0.6);
            ForceLayeredRedraw();
        }

        // ── Track mode (circle / square lens that follows cursor) ─────────────

        public enum TrackMode { Fixed, Circle, Square }
        private TrackMode _trackMode = TrackMode.Fixed;
        private int       _trackSize = 300;

        public TrackMode CurrentTrackMode => _trackMode;

        /// <summary>
        /// Switch between fixed rectangular lens, circular lens, and rounded-square lens.
        /// Circle and Square modes hide the title bar and clip the window to the shape.
        /// The lens position is driven by MainWindow's timer tick (MoveToFollow).
        /// </summary>
        public void SetTrackMode(TrackMode mode, int size)
        {
            _trackMode = mode;
            _trackSize = Math.Max(100, Math.Min(800, size));

            if (mode == TrackMode.Fixed)
            {
                // Restore normal rectangular window — clear Win32 region and WPF clips
                if (_hwndSource != null)
                    SetWindowRgn(_hwndSource.Handle, IntPtr.Zero, true);
                this.Clip        = null;
                OuterBorder.Clip = null;
                RootGrid.Clip    = null;
                OuterBorder.CornerRadius    = new CornerRadius(4);
                // Restore the user's actual border/grip settings, not hardcoded
                // defaults — this branch runs on every startup via RestoreTrackModeUi,
                // so hardcoding here previously stomped saved LensBorderThick/LensGripSize.
                OuterBorder.BorderThickness = new Thickness(_desiredBorderThickness);
                OuterBorder.Background      = (Brush)TryFindResource("BrushBg")
                                              ?? new SolidColorBrush(Color.FromRgb(30, 30, 46));
                TitleRow.Height = new GridLength(TitleHeights[_titleSizeIndex]);
                RootGrid.RowDefinitions[2].Height    = new GridLength(_desiredGripSize);
                RootGrid.ColumnDefinitions[0].Width  = new GridLength(8);
                RootGrid.ColumnDefinitions[2].Width  = new GridLength(8);
                ResizeMode = System.Windows.ResizeMode.CanResizeWithGrip;
                MinHeight  = TitleHeights[_titleSizeIndex] + 60;
            }
            else
            {
                Width     = _trackSize;
                Height    = _trackSize;
                MinHeight = 100;

                // Hide title bar and resize grip rows
                TitleRow.Height                   = new GridLength(0);
                RootGrid.RowDefinitions[2].Height = new GridLength(0);

                // Collapse the 8px resize grip columns so MagHostBorder fills the whole window
                RootGrid.ColumnDefinitions[0].Width = new GridLength(0);
                RootGrid.ColumnDefinitions[2].Width = new GridLength(0);

                // Transparent border so corners don't show BrushBg fill
                OuterBorder.BorderThickness = new Thickness(2);
                OuterBorder.Background      = Brushes.Transparent;

                ApplyTrackClip();
            }
        }

        private void ApplyTrackClip()
        {
            if (_hwndSource == null) return;
            IntPtr hwnd = _hwndSource.Handle;

            // Convert logical size to physical pixels for the Win32 region APIs
            int physSize = (int)Math.Round(_trackSize * DpiScaleX);

            if (_trackMode == TrackMode.Circle)
            {
                // CreateEllipticRgn clips the window to a true ellipse at the OS level.
                // This cuts the actual window region — no square corners can show through.
                // The previous WPF Clip approach only clips WPF rendering, not the window frame.
                IntPtr rgn = CreateEllipticRgn(0, 0, physSize, physSize);
                if (SetWindowRgn(hwnd, rgn, true) == 0)
                    DeleteObject(rgn); // SetWindowRgn failed — we still own it
                // On success, OS owns the region handle — do not DeleteObject it.

                // Remove border stroke — same bleed issue as square mode.
                OuterBorder.BorderThickness = new Thickness(0);

                // WPF clip for the content inside
                var ellipse = new System.Windows.Media.EllipseGeometry(
                    new Point(_trackSize / 2.0, _trackSize / 2.0),
                    _trackSize / 2.0 - 1, _trackSize / 2.0 - 1);
                this.Clip        = ellipse;
                OuterBorder.Clip = new System.Windows.Media.EllipseGeometry(
                    new Point(_trackSize / 2.0, _trackSize / 2.0),
                    _trackSize / 2.0 - 1, _trackSize / 2.0 - 1);
                OuterBorder.CornerRadius = new CornerRadius(_trackSize / 2.0);
            }
            else if (_trackMode == TrackMode.Square)
            {
                double cornerR  = _trackSize * 0.12;
                // CreateRoundRectRgn takes the width and height of the corner ellipse.
                // cornerR is the radius in logical px; the ellipse diameter in physical px
                // is cornerR * 2 * DpiScaleX. Previously cornerPx was the radius in physical
                // px, and we passed cornerPx*2, making corners twice as large as the WPF clip.
                int cornerEllipse = (int)Math.Round(cornerR * 2 * DpiScaleX);

                IntPtr rgn = CreateRoundRectRgn(0, 0, physSize + 1, physSize + 1,
                                                cornerEllipse, cornerEllipse);
                if (SetWindowRgn(hwnd, rgn, true) == 0)
                    DeleteObject(rgn); // SetWindowRgn failed — we still own the handle

                // Remove the border stroke entirely in square track mode — a Border draws
                // its stroke centred on its edge, so half the stroke escapes the Clip and
                // appears as a straight-edged artifact at each corner.
                OuterBorder.BorderThickness = new Thickness(0);

                // WPF clip so rendered content doesn't bleed into corners.
                // Inset by 1px so the geometry edge is fully inside the Win32 region.
                var roundedClip = new System.Windows.Media.RectangleGeometry(
                    new Rect(0, 0, _trackSize, _trackSize), cornerR, cornerR);
                this.Clip        = roundedClip;
                OuterBorder.Clip = new System.Windows.Media.RectangleGeometry(
                    new Rect(0, 0, _trackSize, _trackSize), cornerR, cornerR);
                OuterBorder.CornerRadius = new CornerRadius(cornerR);
            }
        }

        /// <summary>
        /// Move the lens so its centre is at the given LOGICAL screen coordinates.
        /// Called every timer tick in track mode. Uses DPI scale to convert from
        /// physical cursor coordinates to logical WPF window position.
        /// </summary>
        public void MoveToFollow(int physicalX, int physicalY)
        {
            double logX = physicalX / DpiScaleX;
            double logY = physicalY / DpiScaleY;
            Left = logX - Width  / 2.0;
            Top  = logY - Height / 2.0;
        }

        public void SetTrackSize(int size)
        {
            _trackSize = Math.Max(100, Math.Min(800, size));
            if (_trackMode != TrackMode.Fixed)
            {
                Width  = _trackSize;
                Height = _trackSize;
                ApplyTrackClip();
            }
        }

        // ── Dock ──────────────────────────────────────────────────────────────
        public enum DockCorner { None, TopLeft, TopRight, BottomLeft, BottomRight }
        public void DockTo(DockCorner corner)
        {
            if (corner == DockCorner.None) return;
            int cx = (int)(Left * DpiScaleX) + (int)(Width  * DpiScaleX / 2);
            int cy = (int)(Top  * DpiScaleY) + (int)(Height * DpiScaleY / 2);
            var mon = MonitorHelper.MonitorFromPoint(cx, cy);
            double wl = mon.WorkArea.Left   / DpiScaleX, wt = mon.WorkArea.Top    / DpiScaleY;
            double wr = mon.WorkArea.Right  / DpiScaleX, wb = mon.WorkArea.Bottom / DpiScaleY;
            switch (corner)
            {
                case DockCorner.TopLeft:     Left = wl;         Top = wt;          break;
                case DockCorner.TopRight:    Left = wr - Width; Top = wt;          break;
                case DockCorner.BottomLeft:  Left = wl;         Top = wb - Height; break;
                case DockCorner.BottomRight: Left = wr - Width; Top = wb - Height; break;
            }
        }
        private void DockTL_Click(object s, RoutedEventArgs e) => DockTo(DockCorner.TopLeft);
        private void DockTR_Click(object s, RoutedEventArgs e) => DockTo(DockCorner.TopRight);
        private void DockBL_Click(object s, RoutedEventArgs e) => DockTo(DockCorner.BottomLeft);
        private void DockBR_Click(object s, RoutedEventArgs e) => DockTo(DockCorner.BottomRight);

        // ── WndProc ───────────────────────────────────────────────────────────
        private const int WM_MOUSEACTIVATE  = 0x0021;
        private const int WM_NCHITTEST_MSG  = 0x0084;
        private const int HTCAPTION         = 2;
        private const int HTTRANSPARENT     = -1;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                               IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(3); }

            if (msg == WM_NCHITTEST_MSG)
            {
                if (_clickThrough)
                {
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }

                int sx = (short)(lParam.ToInt32() & 0xFFFF);
                int sy = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                var pt = PointFromScreen(new Point(sx, sy));

                double titleH = TitleRow.ActualHeight > 0
                    ? TitleRow.ActualHeight
                    : TitleHeights[_titleSizeIndex];

                if (pt.Y >= 0 && pt.Y < titleH && pt.X >= 0 && pt.X < ActualWidth)
                {
                    var hit = InputHitTest(pt);
                    bool isButton = hit is System.Windows.Controls.Button ||
                                    (hit is DependencyObject d &&
                                     FindAncestorButton(d) != null);
                    if (!isButton)
                    {
                        handled = true;
                        return new IntPtr(HTCAPTION);
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static System.Windows.Controls.Button FindAncestorButton(DependencyObject d)
        {
            var parent = VisualTreeHelper.GetParent(d);
            while (parent != null)
            {
                if (parent is System.Windows.Controls.Button b) return b;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }


        // Title bar right-click context menu handlers
        private void TitleBar_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Update checkable state before showing
            if (CtxThru != null) CtxThru.IsChecked = _clickThrough;
            if (CtxFreeze != null) CtxFreeze.Header = _frozen ? "Resume" : "Freeze";
            if (CtxFreezeIcon != null) CtxFreezeIcon.Source = JtIcons.Get(_frozen ? "queueNow" : "freeze");
        }
        private void CtxFreeze_Click(object sender, RoutedEventArgs e) => ToggleFreeze();
        private void CtxPick_Click  (object sender, RoutedEventArgs e) => ToggleColourPick();
        private void CtxSnap_Click  (object sender, RoutedEventArgs e) => SnapBtn_Click(sender, e);
        private void CtxThru_Click  (object sender, RoutedEventArgs e) => ClickThruBtn_Click(sender, e);

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        // ── Resize ────────────────────────────────────────────────────────────
        private const uint WM_SYSCOMMAND = 0x0112;
        private const int  SC_MOVE       = 0xF010;   // native drag — works with WS_EX_NOACTIVATE
        private enum ResizeDir : int
        { Left=0xF001, Right=0xF002, Top=0xF003, TopLeft=0xF004,
          TopRight=0xF005, Bottom=0xF006, BottomLeft=0xF007, BottomRight=0xF008 }
        private void BeginResize(ResizeDir d)
        { ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle,
            WM_SYSCOMMAND, new IntPtr((int)d), IntPtr.Zero); }
        private void ResizeW_MouseLeftButtonDown (object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.Left);
        private void ResizeE_MouseLeftButtonDown (object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.Right);
        private void ResizeS_MouseLeftButtonDown (object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.Bottom);
        private void ResizeSW_MouseLeftButtonDown(object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.BottomLeft);
        private void ResizeSE_MouseLeftButtonDown(object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.BottomRight);
        private void ResizeNW_MouseLeftButtonDown(object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.TopLeft);
        private void ResizeNE_MouseLeftButtonDown(object s, MouseButtonEventArgs e) => BeginResize(ResizeDir.TopRight);

        protected override void OnClosed(EventArgs e)
        {
            Loaded -= OnLoaded;
            _hwndSource?.RemoveHook(WndProc);
            base.OnClosed(e);
        }
    }
}
