using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

#nullable enable


namespace FileCopy
{
    /// <summary>Two-way: enum value ↔ string. Used by ComboBoxItem.Tag bindings.</summary>
    public sealed class EnumStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && targetType != null && Enum.TryParse(targetType, s, ignoreCase: true, out object? result))
                return result;
            return Binding.DoNothing;
        }
    }

    /// <summary>false → Visible, true → Collapsed.</summary>
    /// <summary>true → false; false/null → true. Used to lock "Verify" while Move is on (FC-01).</summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not true;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not true;
    }

    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v != Visibility.Visible;
    }
}
