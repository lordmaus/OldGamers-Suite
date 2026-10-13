using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

#nullable enable

namespace FileCopy
{
    public partial class PresetSlotDialog : Window
    {
        public int SelectedSlot { get; private set; } = 1;

        public PresetSlotDialog() { InitializeComponent(); }

        private void Slot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag && int.TryParse(tag, out int n))
            {
                SelectedSlot = n;
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Drag handle — same pattern as MainWindow.xaml.cs / AboutDialog.xaml.cs,
        // shared NativeMethods (see MainWindow.xaml.cs).
        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            const int HTBOTTOMRIGHT = 17;
            const int WM_NCLBUTTONDOWN = 0xA1;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, WM_NCLBUTTONDOWN, (nint)HTBOTTOMRIGHT, 0);
        }

        // WCAG 2.5.7 — keyboard alternative for the drag-resize handle.
        private void ResizeHandle_KeyDown(object sender, KeyEventArgs e)
        {
            double step = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 10;
            switch (e.Key)
            {
                case Key.Right: Width  = Math.Max(MinWidth,  Width  + step); e.Handled = true; break;
                case Key.Left:  Width  = Math.Max(MinWidth,  Width  - step); e.Handled = true; break;
                case Key.Down:  Height = Math.Max(MinHeight, Height + step); e.Handled = true; break;
                case Key.Up:    Height = Math.Max(MinHeight, Height - step); e.Handled = true; break;
            }
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("PresetSlotDialog", 400, 240);
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("PresetSlotDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
