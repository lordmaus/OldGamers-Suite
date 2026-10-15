using System;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace BadgeOverlay
{
    /// <summary>
    /// Tray-icon and app-exit code for <see cref="MainWindow"/>.
    /// Split out of MainWindow.xaml.cs so tray-related changes
    /// (icon load logic, context menu reorganisation, exit teardown
    /// ordering) don't churn the main code-behind file.
    ///
    /// The <c>_tray</c> NotifyIcon field still lives on the main
    /// partial — it's referenced by <see cref="MainWindow.UpdateStatusBar"/>
    /// for tray-tooltip updates and by the dispose path. Keeping the
    /// field there avoids forcing every consumer to know which partial
    /// owns it.
    /// </summary>
    public partial class MainWindow
    {
        // ── Tray icon ─────────────────────────────────────────────────────────
        private void SetupTray()
        {
            _tray = new NotifyIcon { Text = "Badge Overlay", Visible = true };
            try
            {
                // Preferred path: load from the EXE's own embedded icon
                // resource. csproj's <ApplicationIcon>app.ico</ApplicationIcon>
                // bakes the .ico into the binary, so this is guaranteed to
                // work as long as the EXE loaded at all — independent of
                // whether app.ico happens to sit beside the EXE on disk.
                // ExtractAssociatedIcon returns the shell "small" variant
                // (16 px at 100 % DPI, scaled on HiDPI).
                System.Drawing.Icon icon = null;
                string exePath = System.Diagnostics.Process.GetCurrentProcess()
                    .MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    try { icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath); }
                    catch (Exception ex) { AppLog.Error("Tray icon ExtractAssociatedIcon", ex); }
                }

                // Fallback: loose app.ico beside the EXE. Covers XCOPY
                // deploys and dev-loop runs where the .ico file is newer
                // than the baked resource.
                if (icon == null)
                {
                    string ico = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                    if (System.IO.File.Exists(ico))
                    {
                        try { icon = new System.Drawing.Icon(ico, 16, 16); }
                        catch (Exception ex) { AppLog.Error("Tray icon file load", ex); }
                    }
                    else
                    {
                        AppLog.Warn($"Tray icon: app.ico missing at {ico}");
                    }
                }

                _tray.Icon = icon ?? System.Drawing.SystemIcons.Application;
            }
            catch (Exception ex)
            {
                AppLog.Error("SetupTray", ex);
                _tray.Icon = System.Drawing.SystemIcons.Application;
            }

            // Tray menu — ordered action-first → settings → about → exit
            // per Microsoft Style Guide (separators between concern groups).
            // _tray.DoubleClick still maps to "Show Panel" so the most common
            // action is reachable without right-clicking.
            var menu = new ContextMenuStrip();

            // Group 1 — primary actions
            var miShow = new ToolStripMenuItem("Show Panel");
            miShow.ShortcutKeyDisplayString = "Double-click";
            miShow.Click += (s, e) => { Show(); Activate(); };
            menu.Items.Add(miShow);

            var miToggleOverlay = new ToolStripMenuItem("Pause overlay");
            miToggleOverlay.Click += (s, e) =>
            {
                _s.OverlayEnabled = !_s.OverlayEnabled;
                if (TglEnable != null) TglEnable.IsChecked = _s.OverlayEnabled;
                _overlay?.ApplySettings(_s);
                PushSettings();
                UpdateStatusBar();
            };
            // Refresh the label whenever the menu opens so the verb matches
            // the current state — toggling between "Pause overlay" and
            // "Resume overlay" is clearer than a static Enable/Disable.
            menu.Opening += (s, e) =>
            {
                string verb = _s.OverlayEnabled ? "Pause overlay" : "Resume overlay";
                // Show the registered chord if one is configured so
                // users can learn the hotkey from the tray without
                // opening Hotkeys window.
                if (_s.HotkeyToggleEnabled && _s.HotkeyToggleVK != 0)
                {
                    string chord = FormatChord(_s.HotkeyToggleMods, _s.HotkeyToggleVK);
                    miToggleOverlay.ShortcutKeyDisplayString = chord;
                }
                miToggleOverlay.Text = verb;
            };
            menu.Items.Add(miToggleOverlay);

            menu.Items.Add(new ToolStripSeparator());

            // Group 2 — settings
            var miSettings = new ToolStripMenuItem("Open settings…");
            miSettings.Click += (s, e) => { Show(); Activate(); };
            menu.Items.Add(miSettings);

            menu.Items.Add(new ToolStripSeparator());

            // Group 3 — about
            var miAbout = new ToolStripMenuItem("About Badge Overlay…");
            miAbout.Click += (s, e) =>
            {
                Show();
                Activate();
                BtnAbout_Click(this, new RoutedEventArgs());
            };
            menu.Items.Add(miAbout);

            menu.Items.Add(new ToolStripSeparator());

            // Group 4 — exit
            menu.Items.Add("Exit", null, (s, e) => ExitApp());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick     += (s, e) => { Show(); Activate(); };
        }

        /// <summary>Second launch of Badge Overlay (single-instance pipe) → show this window.</summary>
        internal void ActivateFromExternal()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        }

        // ── Exit ──────────────────────────────────────────────────────────────
        private void ExitApp()
        {
            _tray?.Dispose();
            TrySaveSettings();
            DisposeRuntimeResources();
            _overlay?.Close();
            _infoOverlay?.Close();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Formats a RegisterHotKey mods+VK pair into a human-readable
        /// chord string for use in tray menu <c>ShortcutKeyDisplayString</c>.
        /// Deliberately matches the format HotkeysWindow uses so there's
        /// no discrepancy (e.g. "Ctrl+Shift+O" not "Ctrl+O+Shift").
        /// </summary>
        private static string FormatChord(uint mods, uint vk)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mods & 0x0002) != 0) parts.Add("Ctrl");
            if ((mods & 0x0004) != 0) parts.Add("Shift");
            if ((mods & 0x0001) != 0) parts.Add("Alt");
            if ((mods & 0x0008) != 0) parts.Add("Win");
            if (vk != 0)
            {
                // Convert VK to display name. Most VKs map cleanly to
                // the character they produce; special cases handled below.
                string key = vk switch
                {
                    0x20 => "Space",
                    0x09 => "Tab",
                    0x08 => "Backspace",
                    0x0D => "Enter",
                    0x1B => "Esc",
                    0x70 => "F1",  0x71 => "F2",  0x72 => "F3",  0x73 => "F4",
                    0x74 => "F5",  0x75 => "F6",  0x76 => "F7",  0x77 => "F8",
                    0x78 => "F9",  0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
                    _ when vk >= 0x41 && vk <= 0x5A => ((char)vk).ToString(),
                    _ when vk >= 0x30 && vk <= 0x39 => ((char)vk).ToString(),
                    _ => $"#{vk:X2}"
                };
                parts.Add(key);
            }
            return string.Join("+", parts);
        }
    }
}
