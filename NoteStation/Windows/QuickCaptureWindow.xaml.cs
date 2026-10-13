using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace NoteStation
{
    public partial class QuickCaptureWindow : Window
    {
        // Raised when user saves. Args: (mode, title, text)
        // mode: "Clip" | "Note" | "Task"
        public event Action<string, string, string> CaptureSubmitted;

        public QuickCaptureWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => { TxtCapture.Focus(); };
            // WCAG 2.1.3: borderless — Alt+Arrow moves, Ctrl+Alt+Arrow resizes, header right-click menu adds the keyboard move/resize loops.
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, DragHandle, allowResize: true);

            // Wire up title placeholder visibility to TxtTitle text changes
            TxtTitle.TextChanged += (_, _) =>
                TitlePlaceholder.Visibility = string.IsNullOrEmpty(TxtTitle.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            TitlePlaceholder.Visibility = Visibility.Visible;
        }

        // Show window near cursor, clamped to screen
        public void ShowNearCursor()
        {
            var pt = System.Windows.Forms.Cursor.Position;
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth;
            double vb = vt + SystemParameters.VirtualScreenHeight;
            Left = Math.Max(vl, Math.Min(pt.X, vr - Width - 20));
            Top  = Math.Max(vt, Math.Min(pt.Y - 30, vb - ActualHeight - 20));
            Show();
            Activate();
            TxtCapture.Focus();
        }

        private string CurrentMode =>
            RbNote.IsChecked == true ? "Note" :
            RbTask.IsChecked == true ? "Task" : "Clip";

        private void RbMode_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtTitle == null || TitleRow == null) return;
            bool showTitle = CurrentMode != "Clip";
            TitleRow.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
            // Clip: single-line; Note: multiline; Task: single-line
            TxtCapture.AcceptsReturn = CurrentMode == "Note";
        }

        private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => Submit();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
            else if (e.Key == Key.Enter && !TxtCapture.AcceptsReturn) { Submit(); e.Handled = true; }
        }

        private void Submit()
        {
            string text = TxtCapture.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            string title = TitleRow.Visibility == Visibility.Visible ? TxtTitle.Text.Trim() : "";
            CaptureSubmitted?.Invoke(CurrentMode, title, text);
            TxtCapture.Clear();
            TxtTitle.Clear();
            Hide();
        }
    }
}
