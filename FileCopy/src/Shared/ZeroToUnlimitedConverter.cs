using System;
using System.Globalization;
using System.Windows.Data;

#nullable enable


namespace FileCopy
{
    /// <summary>Converts 0 → "Unlimited"; other values → "{N} Mbps".</summary>
    public sealed class ZeroToUnlimitedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long l)   return l   == 0 ? "Unlimited" : $"{l} Mbps";
            if (value is int i)    return i   == 0 ? "Unlimited" : $"{i} Mbps";
            if (value is double d) return d   == 0 ? "Unlimited" : $"{d:0} Mbps";
            return "Unlimited";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
