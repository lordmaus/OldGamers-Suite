using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimerStudio
{
    /// <summary>
    /// Converts a string "Visible" or "Collapsed" to the WPF Visibility enum.
    /// Used by ViewModel binding properties that return string visibility values.
    /// </summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public sealed class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
                return s == "Visible" ? Visibility.Visible : Visibility.Collapsed;
            if (value is Visibility v) return v;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible ? "Visible" : "Collapsed";
            return "Collapsed";
        }
    }
}
