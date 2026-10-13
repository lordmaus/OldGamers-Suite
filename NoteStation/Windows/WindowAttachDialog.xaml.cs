using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace NoteStation.Windows
{
    public partial class WindowAttachDialog : Window
    {
        // Win32
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>The pattern selected or typed by the user. Null/empty = clear attachment.</summary>
        public string SelectedPattern { get; private set; } = "";
        /// <summary>True = user clicked Attach/OK.</summary>
        public bool Confirmed { get; private set; } = false;

        public WindowAttachDialog(string currentPattern = "")
        {
            InitializeComponent();
            // WCAG 2.1.3: borderless dialog — Alt+Arrow moves, Ctrl+Alt+Arrow resizes.
            OldGamers.Wpf.Shared.Controls.KeyboardWindowMover.Attach(this, null, allowResize: true);
            TxtPattern.Text = currentPattern ?? "";
            LoadRunningWindows();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Confirmed = false; Close(); } };
        }

        private void LoadRunningWindows()
        {
            var titles = new List<string>();
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString().Trim();
                if (title.Length > 0) titles.Add(title);
                return true;
            }, IntPtr.Zero);
            titles.Sort();
            LstWindows.ItemsSource = titles;
        }

        private void TxtPattern_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            LstWindows.SelectedItem = null; // deselect list when typing manually
        }

        private void LstWindows_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LstWindows.SelectedItem is string title)
                TxtPattern.Text = "*" + title + "*";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedPattern = TxtPattern.Text.Trim();
            Confirmed = true;
            Close();
        }

        private void BtnClearAttach_Click(object sender, RoutedEventArgs e)
        {
            SelectedPattern = "";
            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e)  { Confirmed = false; Close(); }
    }
}
