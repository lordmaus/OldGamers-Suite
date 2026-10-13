using System.Windows;

namespace NoteStation
{
    /// <summary>
    /// Generic "remember last size" behavior for resizable dialogs that don't
    /// already have their own dedicated geometry fields (MainWindow, the
    /// Clip/Task overlays, and StickyNoteWindow persist size on their own
    /// model/AppPrefs fields already — don't attach this to those).
    /// Call <see cref="Attach"/> once per window, right after InitializeComponent().
    /// </summary>
    public static class WindowSizeMemory
    {
        public static void Attach(Window w, string key)
        {
            if (AppPrefs.Instance.DialogSizes.TryGetValue(key, out string saved) &&
                !string.IsNullOrEmpty(saved))
            {
                var parts = saved.Split(',');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], out double sw) &&
                    double.TryParse(parts[1], out double sh) &&
                    sw > 0 && sh > 0)
                {
                    w.Width  = sw;
                    w.Height = sh;
                }
            }

            w.Closing += (_, __) =>
            {
                AppPrefs.Instance.DialogSizes[key] = $"{w.ActualWidth:F0},{w.ActualHeight:F0}";
                AppPrefs.Instance.DeferredSave();
            };
        }
    }
}
