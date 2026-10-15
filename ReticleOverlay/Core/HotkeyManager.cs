using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ReticleOverlay
{
    /// <summary>
    /// Registers / unregisters global hotkeys via RegisterHotKey / UnregisterHotKey.
    /// All hotkeys fire on the WPF Dispatcher thread via HwndSource hook.
    /// P/Invoke lives in Native/User32.cs (NativeMethods).
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        private HwndSource _source;
        private readonly Dictionary<int, Action> _actions = new Dictionary<int, Action>();
        private int _nextId = 9000;

        public void Attach(Window window)
        {
            var helper = new WindowInteropHelper(window);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);
        }

        /// <summary>
        /// Register a hotkey string like "F8", "Ctrl+F8", "Alt+Shift+F8".
        /// Returns the registration ID, or -1 on failure.
        /// </summary>
        public int Register(string hotkeyString, Action callback)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString) || callback == null) return -1;
            if (!TryParse(hotkeyString, out uint mods, out uint vk)) return -1;

            int id = _nextId++;
            bool ok = NativeMethods.RegisterHotKey(_source.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk);
            if (!ok)
            {
                AppLog.Info($"Hotkey '{hotkeyString}' could not be registered (in use by another app?)");
                return -1;
            }
            _actions[id] = callback;
            return id;
        }

        public void UnregisterAll()
        {
            if (_source == null) return;
            foreach (var id in _actions.Keys)
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            _actions.Clear();
            _nextId = 9000;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_actions.TryGetValue(id, out var action))
                { action(); handled = true; }
            }
            return IntPtr.Zero;
        }

        // ── Key string parser ─────────────────────────────────────────────────
        internal static bool TryParse(string s, out uint mods, out uint vk)
        {
            mods = NativeMethods.MOD_NONE; vk = 0;
            var parts = s.Split('+');
            string keyPart = parts[parts.Length - 1].Trim();

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToUpperInvariant())
                {
                    case "CTRL":  case "CONTROL": mods |= NativeMethods.MOD_CTRL;  break;
                    case "ALT":                    mods |= NativeMethods.MOD_ALT;   break;
                    case "SHIFT":                  mods |= NativeMethods.MOD_SHIFT; break;
                    case "WIN":                    mods |= NativeMethods.MOD_WIN;   break;
                }
            }

            // Try to parse as a Key enum
            if (Enum.TryParse(keyPart, true, out Key key))
            {
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                return vk != 0;
            }

            // Fallback: single character -> virtual key via VkKeyScan
            if (keyPart.Length == 1)
            {
                short scan = NativeMethods.VkKeyScan(keyPart[0]);
                if (scan == -1) return false;
                vk = (uint)(scan & 0xFF);
                return vk != 0;
            }

            return false;
        }

        public void Dispose()
        {
            UnregisterAll();
            _source?.RemoveHook(WndProc);
        }
    }
}
