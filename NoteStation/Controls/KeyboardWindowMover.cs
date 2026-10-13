using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace NoteStation.Controls
{
    /// <summary>
    /// Keyboard move/resize for borderless (WindowStyle=None) windows — WCAG 2.1.3 Keyboard (No Exception).
    ///  - Alt+Arrow moves 10px (Alt+Shift+Arrow = 1px)
    ///  - Ctrl+Alt+Arrow resizes 10px (Ctrl+Alt+Shift+Arrow = 1px) when <c>allowResize</c>
    ///  - Adds "Move with keyboard" (SC_MOVE) and, when resizable, "Resize with keyboard" (SC_SIZE)
    ///    to <paramref name="menuHost"/>'s ContextMenu (created if absent; opened with Shift+F10 / Menu key).
    /// Suite-canonical: copy unchanged into other apps (change only the namespace).
    /// </summary>
    public static class KeyboardWindowMover
    {
        private const double Step = 10, FineStep = 1;

        public static void Attach(Window window, FrameworkElement menuHost, bool allowResize)
        {
            ArgumentNullException.ThrowIfNull(window);
            window.PreviewKeyDown += (s, e) => OnPreviewKeyDown(window, e, allowResize);

            if (menuHost == null) return;
            var menu = menuHost.ContextMenu ?? new ContextMenu();
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());

            var move = new MenuItem { Header = "_Move with keyboard", InputGestureText = "Alt+Arrows" };
            System.Windows.Automation.AutomationProperties.SetHelpText(move,
                "Arrow keys move the window, Enter to finish, Escape to cancel");
            move.Click += (_, _) => SysCommand(window, SC_MOVE);
            menu.Items.Add(move);

            if (allowResize)
            {
                var size = new MenuItem { Header = "_Resize with keyboard", InputGestureText = "Ctrl+Alt+Arrows" };
                System.Windows.Automation.AutomationProperties.SetHelpText(size,
                    "Arrow keys resize the window, Enter to finish, Escape to cancel");
                size.Click += (_, _) => SysCommand(window, SC_SIZE);
                menu.Items.Add(size);
            }
            menuHost.ContextMenu = menu;
        }

        private static void OnPreviewKeyDown(Window w, KeyEventArgs e, bool allowResize)
        {
            // Alt turns arrow keys into Key.System; the real key is in SystemKey.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            if ((mods & ModifierKeys.Alt) == 0) return;
            double step = (mods & ModifierKeys.Shift) != 0 ? FineStep : Step;
            double dx = 0, dy = 0;
            switch (key)
            {
                case Key.Left:  dx = -step; break;
                case Key.Right: dx =  step; break;
                case Key.Up:    dy = -step; break;
                case Key.Down:  dy =  step; break;
                default: return;
            }
            if (w.WindowState != WindowState.Normal) return;

            if ((mods & ModifierKeys.Control) != 0)
            {
                if (!allowResize) return;
                AccessibleResizeGrip.Resize(w, dx, dy);
            }
            else
            {
                w.Left += dx;
                w.Top  += dy;
            }
            e.Handled = true;
        }

        /// <summary>Starts the OS keyboard move loop (arrows move, Enter commits, Esc cancels) — for
        /// windows that build their own context menus instead of using <see cref="Attach"/>'s.</summary>
        public static void BeginKeyboardMove(Window w) => SysCommand(w, SC_MOVE);

        /// <summary>Starts the OS keyboard size loop (arrows pick an edge then resize, Enter commits).</summary>
        public static void BeginKeyboardResize(Window w) => SysCommand(w, SC_SIZE);

        private static void SysCommand(Window w, int cmd)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            w.Activate();
            // SC_MOVE/SC_SIZE via WM_SYSCOMMAND start the OS keyboard move/size loop
            // (arrows move, Enter commits, Esc cancels) even without a caption bar.
            PostMessage(hwnd, WM_SYSCOMMAND, (IntPtr)cmd, IntPtr.Zero);
        }

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE = 0xF000;
        private const int SC_MOVE = 0xF010;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
