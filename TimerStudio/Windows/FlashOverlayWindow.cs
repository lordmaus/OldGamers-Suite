using System.Diagnostics;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TimerStudio
{
    public class FlashOverlayWindow : Window
    {
        private readonly DispatcherTimer _autoTimer;
        private readonly Button          _btnDismiss;

        public event EventHandler Dismissed;

        public FlashOverlayWindow(TimerModel model)
        {
            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = new SolidColorBrush(
                                     Color.FromArgb(210,
                                         model.AlertColor.R,
                                         model.AlertColor.G,
                                         model.AlertColor.B));
            Topmost            = true;
            WindowState        = WindowState.Maximized;
            ShowInTaskbar      = false;
            ResizeMode         = ResizeMode.NoResize;
            // Keyboard focus trap: window is focusable and intercepts all keys
            Focusable          = true;
            

            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color       = Colors.Black,
                BlurRadius  = 14,
                Opacity     = 0.65,
                ShadowDepth = 2
            };

            double titleSize = Math.Max(24, model.AlertFontSize * 3.0);
            double subSize   = Math.Max(14, model.AlertFontSize * 1.5);

            var lblName = new TextBlock
            {
                Text                = model.Name,
                FontFamily          = new FontFamily("Segoe UI"),
                FontSize            = titleSize,
                FontWeight          = FontWeights.Bold,
                Foreground          = new SolidColorBrush(model.AlertTitleColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 12),
                Effect              = shadow
            };

            var lblMsg = new TextBlock
            {
                Text                = "Time's up!",
                FontFamily          = new FontFamily("Segoe UI"),
                FontSize            = subSize,
                Foreground          = new SolidColorBrush(model.AlertTitleColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 44),
                Effect              = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 8, Opacity = 0.5, ShadowDepth = 1
                }
            };
            // Assertive: urgent state change — screen reader interrupts to announce immediately
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                lblMsg, System.Windows.Automation.AutomationLiveSetting.Assertive);

            _btnDismiss = new Button
            {
                Content             = "Dismiss  (Esc)",
                FontFamily          = new FontFamily("Segoe UI"),
                FontSize            = 18,
                FontWeight          = FontWeights.SemiBold,
                Foreground          = new SolidColorBrush(model.AlertColor),
                Background          = Brushes.White,
                BorderThickness     = new Thickness(0),
                Padding             = new Thickness(48, 14, 48, 14),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor              = System.Windows.Input.Cursors.Hand,
                MinHeight           = 44,
                MinWidth            = 44
            };
            System.Windows.Automation.AutomationProperties.SetName(_btnDismiss, "Dismiss alert");
            _btnDismiss.Click += (s, e) => DoDismiss();

            var lblCountdown = new TextBlock
            {
                FontFamily          = new FontFamily("Segoe UI"),
                FontSize            = 14,
                Foreground          = new SolidColorBrush(
                                          Color.FromArgb(210,
                                              model.AlertTitleColor.R,
                                              model.AlertTitleColor.G,
                                              model.AlertTitleColor.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 14, 0, 0)
            };
            System.Windows.Automation.AutomationProperties.SetLiveSetting(
                lblCountdown, System.Windows.Automation.AutomationLiveSetting.Polite);

            var stack = new StackPanel
            {
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(lblName);
            stack.Children.Add(lblMsg);
            stack.Children.Add(_btnDismiss);
            if (model.FlashSeconds > 0)
                stack.Children.Add(lblCountdown);

            Content = stack;

            // ── Pulse animation — respects Windows "Show animations" setting ───
            if (SystemParameters.ClientAreaAnimation)
            {
                var anim = new DoubleAnimation
                {
                    From           = 0.88,
                    To             = 0.52,
                    Duration       = TimeSpan.FromMilliseconds(600),
                    AutoReverse    = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                BeginAnimation(OpacityProperty, anim);
            }
            else
            {
                Opacity = 0.88;
            }

            // ── Auto-dismiss ──────────────────────────────────────────────────
            if (model.FlashSeconds > 0)
            {
                int remaining = model.FlashSeconds;
                lblCountdown.Text = "Auto-dismiss in " + remaining + "s";
                _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _autoTimer.Tick += (s, e) =>
                {
                    remaining--;
                    lblCountdown.Text = remaining > 0
                        ? "Auto-dismiss in " + remaining + "s"
                        : "Dismissing…";
                    if (remaining <= 0) { _autoTimer.Stop(); DoDismiss(); }
                };
                _autoTimer.Start();
            }

            // ── Window-level UIA identity ─────────────────────────────────────
            System.Windows.Automation.AutomationProperties.SetName(this,
                $"Timer Alert: {model.Name}");
            System.Windows.Automation.AutomationProperties.SetHelpText(this,
                "Fullscreen timer alert. Press Escape or click Dismiss to close.");

            MouseDown += (s, e) => DoDismiss();
            KeyDown   += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) DoDismiss(); };

            // ── Programmatic keyboard focus trap ──────────────────────────────
            Loaded += (s, e) =>
            {
                Activate();
                _btnDismiss.Focus();
            };
        }

        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
            => new FlashOverlayWindowAutomationPeer(this);

        private void DoDismiss()
        {
            _autoTimer?.Stop();
            BeginAnimation(OpacityProperty, null);
            // M-5: Close() first (in try/catch), then fire Dismissed.
            // If Close throws, Dismissed still fires so the timer state machine
            // can advance. Previously Dismissed fired first then Close — if Close
            // threw, the fullscreen overlay stayed open with no way to dismiss.
            try { Close(); } catch { }
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
