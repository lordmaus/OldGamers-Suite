using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OldGamers.Wpf.Shared
{
    /// <summary>
    /// Themed replacement for <see cref="MessageBox"/> (S-13, 2026-10-13). Same overloads and the same
    /// <see cref="MessageBoxResult"/> return values, so call sites swap <c>MessageBox.Show(</c> →
    /// <c>AppDialog.Show(</c> with no other change.
    ///
    /// Uses the app's theme tokens (BrushBg/Surf/Text/Divider), AppFontFamily, FontSizeBody and the
    /// FlatButton/PrimaryButton styles, so it follows theme, high contrast, UI font and the WCAG AAA
    /// rules: 44 px buttons, keyboard focus on the default (safe) button, Esc = Cancel / No,
    /// Enter = default, Ctrl+C copies the text, window HelpText = message for screen readers.
    ///
    /// Falls back to the Win32 MessageBox when no themed application is running yet (early startup,
    /// shutdown, theme resources not loaded) so a dialog is never lost.
    /// </summary>
    public static class AppDialog
    {
        // ── MessageBox-compatible overloads ──────────────────────────────────
        public static MessageBoxResult Show(string text) =>
            Show(null, text, "", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
        public static MessageBoxResult Show(string text, string caption) =>
            Show(null, text, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button) =>
            Show(null, text, caption, button, MessageBoxImage.None, MessageBoxResult.None);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon) =>
            Show(null, text, caption, button, icon, MessageBoxResult.None);
        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon,
                                            MessageBoxResult defaultResult) =>
            Show(null, text, caption, button, icon, defaultResult);

        public static MessageBoxResult Show(Window owner, string text) =>
            Show(owner, text, "", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
        public static MessageBoxResult Show(Window owner, string text, string caption) =>
            Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button) =>
            Show(owner, text, caption, button, MessageBoxImage.None, MessageBoxResult.None);
        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button,
                                            MessageBoxImage icon) =>
            Show(owner, text, caption, button, icon, MessageBoxResult.None);

        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button,
                                            MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted)
                return Fallback(owner, text, caption, button, icon, defaultResult);
            if (!app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => Show(owner, text, caption, button, icon, defaultResult));
            if (app.TryFindResource("BrushBg") is not Brush)
                return Fallback(owner, text, caption, button, icon, defaultResult);

            try { return ShowThemed(owner, text ?? "", caption ?? "", button, icon, defaultResult); }
            catch (Exception) { return Fallback(owner, text, caption, button, icon, defaultResult); }
        }

        // ── Implementation ───────────────────────────────────────────────────
        private static MessageBoxResult Fallback(Window owner, string text, string caption, MessageBoxButton button,
                                                 MessageBoxImage icon, MessageBoxResult def) =>
            owner != null && owner.IsLoaded
                ? MessageBox.Show(owner, text ?? "", caption ?? "", button, icon, def)
                : MessageBox.Show(text ?? "", caption ?? "", button, icon, def);

        private static MessageBoxResult ShowThemed(Window owner, string text, string caption, MessageBoxButton button,
                                                   MessageBoxImage icon, MessageBoxResult def)
        {
            owner ??= Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                      ?? Application.Current.MainWindow;
            if (owner != null && (!owner.IsLoaded || !owner.IsVisible)) owner = null;

            var choices = Choices(button);
            if (def == MessageBoxResult.None || !choices.Contains(def)) def = choices[0];
            // What closing the window (X / Alt+F4 / Esc) means — same as MessageBox where it allows closing.
            MessageBoxResult closeResult = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
                                         : choices.Contains(MessageBoxResult.No)     ? MessageBoxResult.No
                                         : MessageBoxResult.OK;

            var result = closeResult;
            var win = new Window
            {
                Title = string.IsNullOrEmpty(caption) ? (owner?.Title ?? "") : caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                MinWidth = 320, MaxWidth = 600,
                ShowInTaskbar = owner == null,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Topmost = owner?.Topmost ?? false,
            };
            if (owner != null) win.Owner = owner;
            win.SetResourceReference(Control.BackgroundProperty, "BrushBg");
            win.SetResourceReference(Control.ForegroundProperty, "BrushText");
            Ref(win, Control.FontFamilyProperty, "AppFontFamily");
            AutomationProperties.SetName(win, win.Title);
            AutomationProperties.SetHelpText(win, text);

            var root = new Grid { Margin = new Thickness(20, 18, 20, 16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var glyph = Icon(icon);
            if (glyph != null)
            {
                glyph.Margin = new Thickness(0, 2, 14, 0);
                glyph.VerticalAlignment = VerticalAlignment.Top;
                root.Children.Add(glyph);
            }

            var body = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                LineHeight = double.NaN,
                VerticalAlignment = VerticalAlignment.Center,
            };
            body.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            Ref(body, TextBlock.FontSizeProperty, "FontSizeBody", 14d);
            Grid.SetColumn(body, 1);
            root.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                                           Margin = new Thickness(0, 18, 0, 0) };
            Grid.SetRow(buttons, 1);
            Grid.SetColumnSpan(buttons, 2);
            root.Children.Add(buttons);

            Button focusTarget = null;
            foreach (var choice in choices)
            {
                var b = new Button
                {
                    Content = Label(choice),
                    MinWidth = 96, MinHeight = 44,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(16, 6, 16, 6),
                    IsDefault = choice == def,
                    IsCancel = choice == closeResult && choices.Length > 1,
                };
                string styleKey = choice is MessageBoxResult.OK or MessageBoxResult.Yes ? "PrimaryButton" : "FlatButton";
                if (Application.Current.TryFindResource(styleKey) is Style st) b.Style = st;
                AutomationProperties.SetName(b, Label(choice));
                var c = choice;
                b.Click += (_, _) => { result = c; win.DialogResult = true; };
                buttons.Children.Add(b);
                if (choice == def) focusTarget = b;
            }

            var surface = new Border { Child = root };
            surface.SetResourceReference(Border.BackgroundProperty, "BrushSurf");
            win.Content = surface;

            win.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && choices.Length == 1) { result = choices[0]; win.DialogResult = true; e.Handled = true; }
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    try { Clipboard.SetText($"{win.Title}{Environment.NewLine}{Environment.NewLine}{text}"); } catch { }
                    e.Handled = true;
                }
            };
            win.Loaded += (_, _) => focusTarget?.Focus();

            win.ShowDialog();
            return result;
        }

        private static MessageBoxResult[] Choices(MessageBoxButton button) => button switch
        {
            MessageBoxButton.OKCancel    => new[] { MessageBoxResult.OK,  MessageBoxResult.Cancel },
            MessageBoxButton.YesNo       => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _                            => new[] { MessageBoxResult.OK },
        };

        private static string Label(MessageBoxResult r) => r switch
        {
            MessageBoxResult.Yes    => "_Yes",
            MessageBoxResult.No     => "_No",
            MessageBoxResult.Cancel => "Cancel",
            _                       => "OK",
        };

        /// <summary>Theme-independent fixed-colour glyphs (same convention as the JT icon set).</summary>
        private static FrameworkElement Icon(MessageBoxImage icon)
        {
            Brush fill; string geometry; Brush mark = Brushes.White;
            switch (icon)
            {
                case MessageBoxImage.Warning:   // also Exclamation
                    fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); mark = Brushes.Black;
                    geometry = "M16,2 L31,29 H1 Z"; break;
                case MessageBoxImage.Error:     // also Hand / Stop
                    fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
                    geometry = "M16,1 A15,15 0 1 1 15.99,1 Z"; break;
                case MessageBoxImage.Question:
                case MessageBoxImage.Information:   // also Asterisk
                    fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x8B, 0xF7));
                    geometry = "M16,1 A15,15 0 1 1 15.99,1 Z"; break;
                default: return null;
            }
            var g = new Grid { Width = 32, Height = 32 };
            g.Children.Add(new Path { Data = Geometry.Parse(geometry), Fill = fill, Stretch = Stretch.None });
            string sym = icon switch
            {
                MessageBoxImage.Warning  => "!",
                MessageBoxImage.Error    => "×",
                MessageBoxImage.Question => "?",
                _                        => "i",
            };
            g.Children.Add(new TextBlock
            {
                Text = sym, Foreground = mark, FontWeight = FontWeights.Bold, FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = icon == MessageBoxImage.Warning ? new Thickness(0, 8, 0, 0) : new Thickness(0),
            });
            AutomationProperties.SetName(g, icon switch
            {
                MessageBoxImage.Warning  => "Warning",
                MessageBoxImage.Error    => "Error",
                MessageBoxImage.Question => "Question",
                _                        => "Information",
            });
            return g;
        }

        private static void Ref(FrameworkElement el, DependencyProperty dp, string key, object fallback = null)
        {
            if (Application.Current?.TryFindResource(key) != null) el.SetResourceReference(dp, key);
            else if (fallback != null) el.SetValue(dp, fallback);
        }
    }
}
