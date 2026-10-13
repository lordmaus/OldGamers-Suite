using System.Windows;
using System.Windows.Controls;

namespace NoteStation
{
    /// <summary>
    /// Attached property that lets a plain XAML-bound TextBlock render a stored
    /// clip/note/task field (plain text, or XAML-formatted rich text) with its
    /// Bold/Italic/Underline/Color/Highlight formatting intact, instead of just
    /// dumping the raw string. Used by Clip/Note/Task ItemTemplates in MainWindow.xaml.
    /// </summary>
    public static class RichTextDisplay
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached(
                "FormattedText",
                typeof(string),
                typeof(RichTextDisplay),
                new PropertyMetadata(null, OnFormattedTextChanged));

        public static string GetFormattedText(DependencyObject obj) => (string)obj.GetValue(FormattedTextProperty);
        public static void SetFormattedText(DependencyObject obj, string value) => obj.SetValue(FormattedTextProperty, value);

        public static readonly DependencyProperty MaxCharsProperty =
            DependencyProperty.RegisterAttached(
                "MaxChars",
                typeof(int),
                typeof(RichTextDisplay),
                new PropertyMetadata(160));

        public static int GetMaxChars(DependencyObject obj) => (int)obj.GetValue(MaxCharsProperty);
        public static void SetMaxChars(DependencyObject obj, int value) => obj.SetValue(MaxCharsProperty, value);

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            string stored = e.NewValue as string;
            if (string.IsNullOrEmpty(stored)) return;
            int maxChars = GetMaxChars(tb);
            foreach (var inline in RichTextFormatting.BuildPreviewInlines(stored, maxChars))
                tb.Inlines.Add(inline);
        }
    }
}
