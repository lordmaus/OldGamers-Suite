using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Media;
using WpfButton      = System.Windows.Controls.Button;
using WpfImage       = System.Windows.Controls.Image;
using WpfMenuItem    = System.Windows.Controls.MenuItem;
using WpfStackPanel  = System.Windows.Controls.StackPanel;
using WpfTextBlock   = System.Windows.Controls.TextBlock;
using WpfOrientation = System.Windows.Controls.Orientation;

#nullable enable

namespace TimerStudio
{
    /// <summary>
    /// Lookup for the fixed-color JT icon set in Theming/ColorIcons.xaml (keys "JtIcon.&lt;Name&gt;").
    /// Icons never follow the theme. Always pair with a text label or AutomationProperties.Name.
    /// Same concept = same key across the suite (see NoteStation / FileCopy / BadgeOverlay Core/JtIcons.cs).
    /// WinForms-safe aliases above: UseWindowsForms is on in this project.
    /// </summary>
    public static class JtIcons
    {
        public static ImageSource? Get(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var k = key.Trim();
            return System.Windows.Application.Current?.TryFindResource("JtIcon." + char.ToUpperInvariant(k[0]) + k.Substring(1)) as ImageSource;
        }

        public static WpfImage Img(string key, double size = 16) => new()
        {
            Source  = Get(key),
            Width   = size,
            Height  = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public static WpfMenuItem Menu(string header, string key) => new() { Header = header, Icon = Img(key) };

        /// <summary>Icon + text button content for code-built buttons; also sets the UIA name if none is set.</summary>
        public static WpfStackPanel Label(WpfButton owner, string text, string key, double size = 18)
        {
            var sp = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            var im = Img(key, size);
            im.Margin = new Thickness(0, 0, 7, 0);
            sp.Children.Add(im);
            sp.Children.Add(new WpfTextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (string.IsNullOrEmpty(AutomationProperties.GetName(owner))) AutomationProperties.SetName(owner, text);
            return sp;
        }

        /// <summary>Visible label text → icon key, for combo items whose content is plain text (mode, default sort).</summary>
        private static readonly Dictionary<string, string> LabelKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Countdown"]   = "countdown",
            ["Stopwatch"]   = "stopwatch",
            ["Date / Time"] = "calendar",
            ["Pomodoro"]    = "pomodoro",
            ["Interval"]    = "repeat",
            ["By time remaining (ascending)"]  = "stopwatch",
            ["By time remaining (descending)"] = "stopwatch",
            ["By name (A → Z)"] = "rename",
            ["By name (Z → A)"] = "rename",
            ["By category"]     = "folder",
        };

        public static string? KeyForLabel(string? label)
            => label != null && LabelKeys.TryGetValue(label.Trim(), out var k) ? k : null;
    }

    /// <summary>Icon key → ImageSource for bindings. Null/empty key → null (no image).</summary>
    public sealed class JtIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
            => JtIcons.Get(value as string ?? p as string);
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>Visible label text → ImageSource (combo ItemTemplate). Unknown label → null (image collapses to 0 width).</summary>
    public sealed class JtLabelIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
            => JtIcons.Get(JtIcons.KeyForLabel(value as string));
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }
}
