using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SuperMagnifier
{
    public partial class FirstRunWizard : Window
    {
        private int _step = 0;

        private static readonly (string Title, string Subtitle, string Icon, string Body, string[] Bullets)[] Steps =
        {
            (
                "Welcome to Super Magnifier LV",
                "Screen magnifier for low vision users",
                "gettingStarted",
                "Super Magnifier LV is a powerful, accessible screen magnifier with over 40 features designed for everyday low-vision use. This short tour covers the essentials.",
                new[] { "Magnify any area of your screen 1.25× – 20×", "Follows your cursor or typing cursor automatically", "Fully keyboard accessible — no mouse required" }
            ),
            (
                "Getting Started",
                "Enabling the magnifier",
                "queueNow",
                "The control panel is always visible on screen. To start magnifying:",
                new[] {
                    "Click Enable Magnifier — or press Ctrl+Alt+M",
                    "A lens window appears. Drag it by its title bar to reposition",
                    "Use the Zoom in / Zoom out buttons or Ctrl+Alt+= / Ctrl+Alt+- to change zoom",
                    "Press Ctrl+Alt+M again (or click Disable Magnifier) to hide the lens"
                }
            ),
            (
                "Keyboard Shortcuts",
                "Control everything without a mouse",
                "keyboard",
                "Every feature has a keyboard shortcut. The most useful ones:",
                new[] {
                    "Ctrl+Alt+M — toggle magnifier on/off",
                    "Ctrl+Alt+F — freeze the current view",
                    "Ctrl+Alt+= / Ctrl+Alt+- — zoom in / out",
                    "Ctrl+Alt+0 — return to your home zoom",
                    "Ctrl+Alt+P — fire cursor-finder pulse rings",
                    "Ctrl+Alt+[ / Ctrl+Alt+] — adjust lens opacity",
                    "Arrow keys / Ctrl+Shift+Arrow — move / resize the lens (while Super Magnifier is active)"
                }
            ),
            (
                "Accessibility Features",
                "Designed for low-vision users",
                "eye",
                "Super Magnifier LV has tools to help you see and navigate more comfortably:",
                new[] {
                    "Reading Ruler — marks a line across the lens to aid reading",
                    "Cursor Highlight — ring around the cursor inside the lens",
                    "9 colour modes — invert, greyscale, deuteranopia, warm amber & more",
                    "Zoom Lock — limit zoom range so you never accidentally zoom too far",
                    "Dim When Idle — fades the lens when you stop moving",
                    "Snap-to-Word — centres the lens on the word under your cursor"
                }
            ),
            (
                "You're Ready!",
                "Explore Settings for more options",
                "complete",
                "That's the tour! Here are a few things to explore next:",
                new[] {
                    "Settings — colour modes, render quality, scroll direction",
                    "Lens — opacity, pan lag, border, zoom lock, dim-when-idle",
                    "Lens Presets — save 5 different configurations (Ctrl+Alt+1–5)",
                    "Hotkeys — view all shortcuts and remap any key to suit your keyboard",
                    "Theme — choose from dark, light, high-contrast and other colour themes",
                    "Press Ctrl+Alt+M now to enable the magnifier and start exploring!"
                }
            ),
        };

        public FirstRunWizard() { InitializeComponent(); ShowStep(0); }

        private void ShowStep(int step)
        {
            _step = step;
            var (title, subtitle, icon, body, bullets) = Steps[step];

            StepTitle.Text    = title;
            StepSubtitle.Text = subtitle;

            // Build step content
            var sp = new StackPanel();

            // Icon
            var stepIcon = JtIcons.Img(icon, 48);
            stepIcon.Margin = new Thickness(0, 0, 0, 12);
            stepIcon.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(stepIcon);

            // Body text
            sp.Children.Add(new TextBlock
            {
                Text = body, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("BrushText"),
                Margin = new Thickness(0, 0, 0, 12), LineHeight = 18,
                Focusable = false,   // non-interactive, exclude from tab order
            });

            // Bullets
            foreach (var b in bullets)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new Ellipse
                {
                    Width = 6, Height = 6, Fill = (Brush)FindResource("BrushBlue"),
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0),
                    IsHitTestVisible = false,   // not interactive
                    Focusable        = false,   // exclude from tab order
                };
                Grid.SetColumn(dot, 0);

                var txt = new TextBlock
                {
                    Text = b, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("BrushText"), LineHeight = 16,
                    Focusable = false,   // non-interactive
                };
                Grid.SetColumn(txt, 1);

                row.Children.Add(dot);
                row.Children.Add(txt);
                sp.Children.Add(row);
            }

            StepContent.Content = new ScrollViewer
            {
                Content = sp,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // Update dots — WCAG 1.4.1: sync AutomationProperties so screen readers
            // announce position without relying on fill colour alone.
            var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5 };
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i].Fill = new SolidColorBrush(
                    i == step ? Colors.White : Color.FromRgb(0x55, 0x99, 0xEE));
                System.Windows.Automation.AutomationProperties.SetName(
                    dots[i], i == step ? $"Step {i + 1} (current)" : $"Step {i + 1}");
            }
            System.Windows.Automation.AutomationProperties.SetName(
                DotPanel, $"Step {step + 1} of {dots.Length}");

            // Nav button state
            BackBtn.IsEnabled = step > 0;
            bool last = step == Steps.Length - 1;
            NextBtnText.Text   = last ? "Finish" : "Next";
            NextBtnIcon.Source = JtIcons.Get(last ? "complete" : "forward");
            System.Windows.Automation.AutomationProperties.SetName(NextBtn, last ? "Finish tour" : "Next step");
            SkipBtn.Visibility= step == Steps.Length - 1
                                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_step < Steps.Length - 1)
                ShowStep(_step + 1);
            else
                Close();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_step > 0) ShowStep(_step - 1);
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e) => Close();

        // WCAG 2.1.2 — Escape closes the wizard without a keyboard trap
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); }
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("SMLVFirstRun", 480, 400);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("SMLVFirstRun", Width, Height);
            base.OnClosed(e);
        }
    }
}