using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NoteStation
{
    /// <summary>
    /// Lookup for the fixed-color JT icon set in Theming/ColorIcons.xaml (keys "JtIcon.&lt;Name&gt;").
    /// Icons never follow the theme. Always pair with a text label or AutomationProperties.Name.
    /// </summary>
    public static class JtIcons
    {
        public const string DefaultFolderKey = "folder";

        // Folder icons persisted before 2026-09-24 were emoji; map them to icon keys.
        private static readonly Dictionary<string, string> LegacyEmoji = new(StringComparer.Ordinal)
        {
            ["📁"] = "folder",    ["📂"] = "open",     ["💼"] = "briefcase", ["🏠"] = "home",
            ["📓"] = "notebook",  ["💡"] = "idea",     ["✅"] = "complete",  ["🎯"] = "focus",
            ["🔖"] = "bookmark",  ["📌"] = "pin",      ["🗂"] = "cardIndex", ["🗂️"] = "cardIndex",
            ["🎨"] = "theme",     ["🔧"] = "wrench",   ["📅"] = "calendar",  ["📷"] = "camera",
            ["⭐"] = "star",      ["🔍"] = "search",   ["🕐"] = "recent",    ["🗄"] = "archive",
            ["🗄️"] = "archive",   ["🗑"] = "delete",   ["🗑️"] = "delete",
        };

        /// <summary>User-folder icon picker: (key, label) in display order.</summary>
        public static readonly (string Key, string Label)[] FolderChoices =
        {
            ("folder", "Folder"),       ("open", "Open folder"),   ("briefcase", "Work"),
            ("home", "Home"),           ("notebook", "Notebook"),  ("idea", "Idea"),
            ("complete", "Done"),       ("focus", "Focus"),        ("bookmark", "Bookmark"),
            ("pin", "Pin"),             ("cardIndex", "Card index"), ("theme", "Art"),
            ("wrench", "Tools"),        ("calendar", "Calendar"),  ("camera", "Photos"),
            ("star", "Star"),           ("database", "Data"),      ("reports", "Reports"),
            ("dashboard", "Stats"),     ("gettingStarted", "Goals"), ("health", "Health"),
            ("user", "Personal"),       ("building", "Office"),    ("contacts", "Contacts"),
            ("modules", "Projects"),    ("tag", "Labels"),         ("refresh", "Recurring"),
            ("deployment", "Shipping"),
        };

        public static string NormalizeKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DefaultFolderKey;
            var s = raw.Trim();
            return LegacyEmoji.TryGetValue(s, out var k) ? k : s;
        }

        public static ImageSource Get(string key)
        {
            var k = NormalizeKey(key);
            var app = Application.Current;
            if (app == null) return null;
            var res = "JtIcon." + char.ToUpperInvariant(k[0]) + k.Substring(1);
            return app.TryFindResource(res) as ImageSource
                ?? app.TryFindResource("JtIcon.Folder") as ImageSource;
        }

        public static System.Windows.Controls.Image Img(string key, double size = 16) => new()
        {
            Source  = Get(key),
            Width   = size,
            Height  = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>Context-menu item with a color icon in the icon column.</summary>
        public static MenuItem Menu(string header, string key) => new() { Header = header, Icon = Img(key) };
    }

    /// <summary>Icon key (or legacy emoji) → ImageSource for bindings.</summary>
    public sealed class JtIconConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => JtIcons.Get(value as string ?? p as string);
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    /// <summary>Folder accent hex → solid bar brush. Empty/invalid = transparent (keeps alignment).</summary>
    public sealed class FolderAccentBarConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not string hex || string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException) { return Brushes.Transparent; }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }
}
