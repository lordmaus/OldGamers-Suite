using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Wraps a WinForms NotifyIcon so the app can minimize to tray during
    /// long copies and show a balloon notification on completion.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _ni;
        private readonly Window     _owner;
        private bool _disposed;

        private EventHandler _doubleClickHandler;

        public TrayIcon(Window owner, Icon? icon)
        {
            _owner = owner;

            _ni = new NotifyIcon
            {
                Text    = "File Copy Studio",
                Icon    = icon ?? SystemIcons.Application,
                Visible = false
            };

            _doubleClickHandler = (_, __) => Restore();
            _ni.DoubleClick += _doubleClickHandler;

            var ctxMenu = new ContextMenuStrip();
            ctxMenu.Items.Add("&Open File Copy Studio",  null, (_, __) => Restore());
            ctxMenu.Items.Add("&New Instance", null, (_, __) =>
            {
                try
                {
                    // Environment.ProcessPath is correct for both regular and single-file publish
                    string? exe = Environment.ProcessPath
                                 ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                }
                catch { }
            });
            ctxMenu.Items.Add(new ToolStripSeparator());
            var exitItem = new ToolStripMenuItem("E&xit", null, (_, __) =>
            {
                _ni.Visible = false;
                owner.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
            }) { ShortcutKeyDisplayString = "Alt+F4" };
            ctxMenu.Items.Add(exitItem);
            _ni.ContextMenuStrip = ctxMenu;
        }

        public void MinimizeToTray()
        {
            _ni.Visible = true;
            _owner.Dispatcher.Invoke(() =>
            {
                _owner.WindowState = WindowState.Minimized;
                _owner.ShowInTaskbar = false;
            });
        }

        public void Restore()
        {
            _owner.Dispatcher.Invoke(() =>
            {
                _owner.ShowInTaskbar = true;
                _owner.WindowState   = WindowState.Normal;
                _owner.Activate();
            });
            _ni.Visible = false;
        }

        public void ShowBalloon(string title, string text,
            ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 4000)
        {
            _ni.Visible = true;
            _ni.ShowBalloonTip(timeoutMs, title, text, icon);
        }

        public bool IsInTray => !_owner.ShowInTaskbar;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ni.DoubleClick -= _doubleClickHandler;
            _doubleClickHandler = null;
            _ni.Visible = false;
            _ni.Dispose();
        }
    }
}
