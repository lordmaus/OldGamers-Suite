using System;
using System.Drawing;
using System.Reflection;
using WinForms = System.Windows.Forms;

namespace ReticleOverlay
{
    /// <summary>
    /// System-tray icon with context menu. Fires events back to MainWindow.
    /// </summary>
    public class TrayIcon : IDisposable
    {
        private WinForms.NotifyIcon  _notify;
        private WinForms.ContextMenuStrip _menu;
        private WinForms.ToolStripMenuItem _itemToggle;

        public event EventHandler ToggleOverlay;
        public event EventHandler ShowWindow;
        public event EventHandler ExitApp;

        public TrayIcon()
        {
            _menu = new WinForms.ContextMenuStrip();

            // Informational version item (greyed, not clickable)
            string ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            try {
                ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(
                    System.Reflection.Assembly.GetExecutingAssembly().Location).ProductVersion ?? ver;
            } catch { }
            var itemVersion = new WinForms.ToolStripMenuItem($"Reticle Overlay  {ver}")
            {
                Enabled = false,
                Font = new System.Drawing.Font("Segoe UI", 8f)
            };

            _itemToggle = new WinForms.ToolStripMenuItem("Hide Reticle");
            _itemToggle.Click += (s,e) => ToggleOverlay?.Invoke(this, EventArgs.Empty);

            var itemExit = new WinForms.ToolStripMenuItem("Exit");
            itemExit.Click += (s,e) => ExitApp?.Invoke(this, EventArgs.Empty);

            _menu.Items.Add(itemVersion);
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(_itemToggle);
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(itemExit);

            _notify = new WinForms.NotifyIcon
            {
                Text            = "Reticle Overlay",
                ContextMenuStrip = _menu,
                Visible         = true
            };

            // Load icon from exe
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                _notify.Icon = Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
            }
            catch { _notify.Icon = SystemIcons.Application; }

            _notify.DoubleClick += (s,e) => ShowWindow?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateToggleLabel(bool reticleVisible)
        {
            if (_itemToggle != null)
                _itemToggle.Text = reticleVisible ? "Hide Reticle" : "Show Reticle";
        }

        // Polish #7: dynamic tray tooltip showing profile + visibility state
        public void UpdateTooltip(string profileName, bool visible)
        {
            try
            {
                string state   = visible ? "Active" : "Hidden";
                string profile = string.IsNullOrEmpty(profileName) ? "" : $" — {profileName}";
                // NotifyIcon.Text is capped at 63 chars by Windows
                string tip = $"Reticle Overlay  •  {state}{profile}";
                _notify.Text = tip.Length > 63 ? tip[..60] + "…" : tip;
            }
            catch { }
        }

        public void ShowBalloon(string title, string text)
        {
            try { _notify.ShowBalloonTip(2000, title, text, WinForms.ToolTipIcon.None); }
            catch { }
        }

        public void Dispose()
        {
            _notify.Visible = false;
            // Bug #10 (2026-09-07 review): Icon.ExtractAssociatedIcon() hands back an
            // Icon that NotifyIcon does not own/dispose on its own -- dispose it explicitly.
            _notify.Icon?.Dispose();
            _menu?.Dispose();
            _notify?.Dispose();
        }
    }
}
