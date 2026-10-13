using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Media;
using WpfButton     = System.Windows.Controls.Button;
using WpfImage      = System.Windows.Controls.Image;
using WpfMenuItem   = System.Windows.Controls.MenuItem;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock  = System.Windows.Controls.TextBlock;
using WpfOrientation = System.Windows.Controls.Orientation;

#nullable enable

namespace BadgeOverlay
{
    /// <summary>
    /// Lookup for the fixed-color JT icon set in Theming/ColorIcons.xaml (keys "JtIcon.&lt;Name&gt;").
    /// Icons never follow the theme. Always pair with a text label or AutomationProperties.Name.
    /// Same concept = same key across the suite (see NoteStation / FileCopy Core/JtIcons.cs).
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

        /// <summary>Icon + text button content for code-built buttons; also sets the UIA name.</summary>
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
    }

    /// <summary>Icon key → ImageSource for bindings. Null/empty key → null (no image).</summary>
    public sealed class JtIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type t, object? p, CultureInfo c)
            => JtIcons.Get(value as string ?? p as string);
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }
}
