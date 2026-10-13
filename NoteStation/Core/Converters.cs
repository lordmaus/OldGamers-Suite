using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NoteStation
{
    // ── Bool → Visibility ─────────────────────────────────────────────────────
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool bv && bv;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility v && v == Visibility.Visible;
    }

    // ── Clip image path (plain or DPAPI .enc) → BitmapSource ─────────────────
    public class ClipImageConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not string path || string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            try { return NoteStation.Core.ClipVault.LoadImage(path); }
            catch (Exception ex) { AppLog.Error("Clip thumbnail", ex); return null; }
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    // ── String (hex color) → Color ────────────────────────────────────────────
    public class StringToColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            try { return (Color)ColorConverter.ConvertFromString(value as string ?? "#808080"); }
            catch { return Colors.Gray; }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Color col ? col.ToString() : "#808080";
    }

    // ── Bool → TextDecorations (strikethrough when true) ──────────────────────
    public class BoolToStrikethroughConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is bool b && b) ? TextDecorations.Strikethrough : null;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── Bool → "Visible" / "Hidden" text ─────────────────────────────────────
    public class BoolToVisibleTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is bool b && b) ? "Visible" : "Hidden";

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value?.ToString() == "Visible";
    }

    // ── Category string → SolidColorBrush (via App.CategoryColor) ────────────
    public class CategoryToColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => new SolidColorBrush(App.CategoryColor(value as string ?? ""));

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── Category string → readable text color for CatPill (luma-based, not hardcoded) ──
    public class CategoryToTextColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => new SolidColorBrush(App.ReadableTextColor(App.CategoryColor(value as string ?? "")));

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── String → Visibility (visible when non-empty) ──────────────────────────
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── D3-4: int (Priority) → Visibility (visible when != 0) ────────────────
    public class NonZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is int i && i != 0) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── D3-4: int (Priority) → SolidColorBrush ───────────────────────────────
    public class PriorityToColorConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return (value is int i ? i : 0) switch
            {
                1 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4FC3F7")),  // Low  = Cyan
                2 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),  // Med  = Amber/Gold
                3 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8A80")),  // High = Red (light: black glyph ≥7:1 on every level, WCAG 1.4.6)
                _ => new SolidColorBrush(Colors.Transparent),
            };
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── D3-4: int (Priority) → label string ──────────────────────────────────
    public class PriorityToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return (value is int i ? i : 0) switch
            {
                1 => "Low priority",
                2 => "Medium priority",
                3 => "High priority",
                _ => "",
            };
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }

    // ── D3-4: int (Priority) → glyph string ──────────────────────────────────
    public class PriorityToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return (value is int i ? i : 0) switch
            {
                1 => "▼",
                2 => "▲",
                3 => "▲▲",
                _ => "",
            };
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => DependencyProperty.UnsetValue;
    }
}
