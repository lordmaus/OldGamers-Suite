using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TimerStudio
{
    /// <summary>
    /// Shared base for LensAlertWindow and LensReminderWindow.
    /// Handles positioning, DPI conversion, pulse animation, auto-dismiss,
    /// stacking offsets, and Escape/click dismissal.
    /// Subclasses build their own content and call InitPopup().
    /// </summary>
    public abstract class LensPopupBase : Window
    {
        // ── Shared stacking counter across all lens popups ─────────────────
        private static int _stackCount = 0;
        private readonly int _stackIndex;

        private DispatcherTimer _autoTimer;
        private DispatcherTimer _pulseTimer;
        private bool _pulseState;

        public event EventHandler Dismissed;

        protected LensPopupBase()
        {
            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = System.Windows.Media.Brushes.Transparent;
            Topmost            = true;
            ShowInTaskbar      = false;
            ResizeMode         = ResizeMode.NoResize;
            SizeToContent      = SizeToContent.WidthAndHeight;
            Focusable          = true;

            _stackIndex = System.Threading.Interlocked.Increment(ref _stackCount);
            Closed += (s, e) =>
            {
                int v = System.Threading.Interlocked.Decrement(ref _stackCount);
                if (v < 0) System.Threading.Interlocked.Increment(ref _stackCount);
            };

            MouseDown += (s, e) => DoDismiss();
            KeyDown   += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) DoDismiss();
            };
        }

        /// <summary>Sets the window-level UIA name. Call from subclass before InitPopup.</summary>
        protected void SetWindowName(string timerName)
        {
            System.Windows.Automation.AutomationProperties.SetName(this,
                $"Timer Alert: {timerName}");
            System.Windows.Automation.AutomationProperties.SetHelpText(this,
                "Timer alert popup. Press Escape or click Dismiss to close.");
        }

        /// <summary>
        /// Call from subclass constructor AFTER setting Content.
        /// Starts timers, positions window.
        /// </summary>
        protected void InitPopup(LensPosition position, int flashSeconds,
                                  TextBlock lblCountdown, int pulseMs = 480)
        {
            Left = -9999; Top = -9999;
            Loaded += (s, e) => PositionWindow(position);

            // Pulse
            Opacity     = 1.0;
            _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(pulseMs) };
            _pulseTimer.Tick += (s, e) =>
            {
                _pulseState = !_pulseState;
                Opacity     = _pulseState ? 0.72 : 1.0;
            };
            _pulseTimer.Start();

            // Auto-dismiss
            if (flashSeconds > 0 && lblCountdown != null)
            {
                int remaining = flashSeconds;
                lblCountdown.Text = "Auto-dismiss in " + remaining + "s";
                _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _autoTimer.Tick += (s, e) =>
                {
                    remaining--;
                    lblCountdown.Text = remaining > 0
                        ? "Auto-dismiss in " + remaining + "s"
                        : "Dismissing\u2026";
                    if (remaining <= 0) { _autoTimer.Stop(); DoDismiss(); }
                };
                _autoTimer.Start();
            }
        }

        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
            => new LensPopupAutomationPeer(this);

        protected void DoDismiss()
        {
            _autoTimer?.Stop();
            _pulseTimer?.Stop();
            // C-6: Close first, then fire Dismissed — mirrors FlashOverlayWindow.
            // Firing Dismissed first advanced timer state even if Close() threw,
            // leaving a dismissed-state timer behind a still-visible alert window.
            try { Close(); } catch { }
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private void PositionWindow(LensPosition pos)
        {
            Rect wa  = GetWorkAreaDips();
            double w   = ActualWidth  > 1 ? ActualWidth  : 324;
            double h   = ActualHeight > 1 ? ActualHeight : 180;
            double pad = 16;
            double x, y;

            switch (pos)
            {
                case LensPosition.TopLeft:      x = wa.Left + pad;           y = wa.Top + pad;            break;
                case LensPosition.TopCenter:    x = wa.Left + (wa.Width-w)/2; y = wa.Top + pad;           break;
                case LensPosition.TopRight:     x = wa.Right - w - pad;      y = wa.Top + pad;            break;
                case LensPosition.MiddleLeft:   x = wa.Left + pad;           y = wa.Top + (wa.Height-h)/2; break;
                case LensPosition.Center:       x = wa.Left + (wa.Width-w)/2; y = wa.Top + (wa.Height-h)/2; break;
                case LensPosition.MiddleRight:  x = wa.Right - w - pad;      y = wa.Top + (wa.Height-h)/2; break;
                case LensPosition.BottomLeft:   x = wa.Left + pad;           y = wa.Bottom - h - pad;     break;
                case LensPosition.BottomCenter: x = wa.Left + (wa.Width-w)/2; y = wa.Bottom - h - pad;    break;
                default:                        x = wa.Right - w - pad;      y = wa.Bottom - h - pad;     break;
            }

            double stackOffset = (_stackIndex - 1) * 12.0;
            Left = x - stackOffset;
            Top  = y - stackOffset;
        }

        private Rect GetWorkAreaDips()
        {
            try
            {
                var cursorPt = System.Windows.Forms.Cursor.Position;
                var screen   = System.Windows.Forms.Screen.FromPoint(cursorPt);
                var physWa   = screen.WorkingArea;
                var src      = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null)
                {
                    double sx = src.CompositionTarget.TransformToDevice.M11;
                    double sy = src.CompositionTarget.TransformToDevice.M22;
                    return new Rect(physWa.Left/sx, physWa.Top/sy,
                                    physWa.Width/sx, physWa.Height/sy);
                }
            }
            catch { }
            return SystemParameters.WorkArea;
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoTimer?.Stop();
            _pulseTimer?.Stop();
            base.OnClosed(e);
        }

        // ── Shared helper: build the styled outer border ───────────────────
        protected static Border MakeOuterBorder(Color alertColor, double width = 320)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(14),
                Background   = new SolidColorBrush(
                    Color.FromArgb(235, alertColor.R, alertColor.G, alertColor.B)),
                Padding = new Thickness(22, 16, 22, 18),
                Width   = width,
                Effect  = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 28, Opacity = 0.6, ShadowDepth = 4
                }
            };
        }

        protected static System.Windows.Media.Effects.DropShadowEffect TextShadow()
            => new System.Windows.Media.Effects.DropShadowEffect
               { Color = Colors.Black, BlurRadius = 6, Opacity = 0.45, ShadowDepth = 1 };

        protected static Button MakeDismissButton(string label, Color alertColor, double fontSize)
        {
            var btn = new Button
            {
                Content             = label,
                FontFamily          = new FontFamily("Segoe UI"),
                FontSize            = Math.Max(11, fontSize),
                FontWeight          = FontWeights.SemiBold,
                Foreground          = new SolidColorBrush(alertColor),
                Background          = System.Windows.Media.Brushes.White,
                BorderThickness     = new Thickness(0),
                Padding             = new Thickness(20, 7, 20, 7),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor              = System.Windows.Input.Cursors.Hand
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, label);
            return btn;
        }

        protected static TextBlock MakeCountdownLabel(Color titleColor, double fontSize)
        {
            var lbl = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = Math.Max(9, fontSize),
                Foreground = new SolidColorBrush(
                    Color.FromArgb(180, titleColor.R, titleColor.G, titleColor.B)),
                Margin = new Thickness(0, 8, 0, 0)
            };
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                lbl, System.Windows.Automation.AutomationLiveSetting.Polite);
            return lbl;
        }
    }
}
