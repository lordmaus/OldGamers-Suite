using System.Windows;

namespace NoteStation.Controls
{
    /// <summary>
    /// Code-side access to the App.xaml type-ramp tokens (FontSizeCaption / FontSizeBody / FontSizeTitle).
    /// WCAG 2.2 AAA (1.4.8, low-vision goal): nothing below Caption = 12px.
    /// XAML uses {DynamicResource FontSizeCaption}; code-constructed text uses these.
    /// </summary>
    public static class TypeRamp
    {
        public static double Caption => Get("FontSizeCaption", 12);
        public static double Body    => Get("FontSizeBody",    14);
        public static double Title   => Get("FontSizeTitle",   18);

        private static double Get(string key, double fallback)
            => Application.Current?.TryFindResource(key) is double d ? d : fallback;
    }
}
