using System;
using System.Windows;
using System.Windows.Input;

#nullable enable

namespace FileCopy
{
    public partial class HotkeysDialog : Window
    {
        public HotkeysDialog()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Drag handle (see MainWindow.xaml.cs — same pattern, NativeMethods shared) ──────────
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
    }
}
