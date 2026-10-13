using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;

namespace TimerStudio
{
    /// <summary>
    /// Compact always-on-top pill strip (Feature 11).
    /// Displays running timer names + countdowns in a 48 px translucent bar.
    /// The user can drag it anywhere; position is not persisted (resets to
    /// bottom-centre on next open — simple and predictable).
    /// </summary>
    public partial class PillWindow : Window
    {
        // ── No-running-timers placeholder text ────────────────────────────────
        private const string _idleText = "No timers running";

        // L-3: shared FontFamily instances — don't allocate per item per Refresh() call
        private static readonly FontFamily _segoeUI    = new FontFamily("Segoe UI");
        private static readonly FontFamily _consolasUI = new FontFamily("Consolas, Segoe UI");

        public PillWindow()
        {
            InitializeComponent();

            // WCAG 2.1.3: borderless pill — Alt+Arrow move + "Move with keyboard" (SC_MOVE) context item.
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, PillRoot, allowResize: false);
            // Defer positioning until Loaded: PresentationSource.FromVisual() returns
            // null before the window is shown, causing wrong DPI scale and off-screen
            // placement on HiDPI displays.
            Loaded += (s, e) => PositionAtBottomCentre();
            Refresh(new List<(string, string)>());
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the pill item strip from the supplied list of
        /// (name, formattedTime) tuples. Call from MainWindow.Tick_Tick.
        /// </summary>
        public void Refresh(List<(string name, string time)> running)
        {
            PillItems.Children.Clear();

            if (running.Count == 0)
            {
                PillItems.Children.Add(MakeIdleLabel());
                return;
            }

            bool first = true;
            foreach (var (name, time) in running)
            {
                if (!first)
                    PillItems.Children.Add(MakeSeparator());
                PillItems.Children.Add(MakePillItem(name, time));
                first = false;
            }
        }

        // ── Layout helpers ────────────────────────────────────────────────────

        private static TextBlock MakeIdleLabel()
            => new TextBlock
            {
                Text              = _idleText,
                Foreground        = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                FontFamily        = _segoeUI,
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 8, 0)
            };

        private static FrameworkElement MakeSeparator()
            => new System.Windows.Shapes.Rectangle
            {
                Width             = 1,
                Fill              = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin            = new Thickness(4, 8, 4, 8)
            };

        private static StackPanel MakePillItem(string name, string time)
        {
            var sp = new StackPanel
            {
                Orientation       = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 6, 0)
            };

            // Name (muted white)
            sp.Children.Add(new TextBlock
            {
                Text              = name,
                Foreground        = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontFamily        = _segoeUI,
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth          = 160,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                Margin            = new Thickness(0, 0, 5, 0)
            });

            // Countdown (bright white, monospace digits)
            sp.Children.Add(new TextBlock
            {
                Text              = time,
                Foreground        = new SolidColorBrush(Colors.White),
                FontFamily        = _consolasUI,
                FontSize          = 13,
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            return sp;
        }

        // ── Positioning ───────────────────────────────────────────────────────

        private void PositionAtBottomCentre()
        {
            // L-4: use the monitor containing the cursor, not PrimaryScreen.
            // PrimaryScreenWidth/Height give wrong coordinates on secondary monitors.
            var cursorPt = System.Windows.Forms.Cursor.Position;
            var screen   = Screen.FromPoint(cursorPt);
            var wa       = screen.WorkingArea;

            double dpiScale = 1.0;
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget != null)
                dpiScale = src.CompositionTarget.TransformToDevice.M11;

            double waWidthDips  = wa.Width  / dpiScale;
            double waBottomDips = (wa.Top + wa.Height) / dpiScale;
            double waLeftDips   = wa.Left   / dpiScale;

            // Width not yet known (SizeToContent=Width); estimate 400px and centre
            Left = waLeftDips + (waWidthDips - 400) / 2;
            Top  = waBottomDips - 60;
        }

        // ── Drag-to-move ─────────────────────────────────────────────────────
        private void PillRoot_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        // ── Close ─────────────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        // H-8: Without _reallyClosing, PillWindow_Closing unconditionally cancels every
        // Close() call — including Application.Shutdown() — and WPF shutdown can stall.
        // MainWindow.OnClosed calls CloseForReal() to bypass the cancel.
        private bool _reallyClosing;

        public void CloseForReal()
        {
            _reallyClosing = true;
            Close();
        }

        // Prevent user Close() — just hide so MainWindow can re-show later.
        private void PillWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_reallyClosing) return;
            e.Cancel = true;
            Hide();
        }
    }
}
