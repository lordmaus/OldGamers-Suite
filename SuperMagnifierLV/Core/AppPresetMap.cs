using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SuperMagnifier
{
    /// <summary>
    /// Watches for foreground window changes and auto-loads a preset when a
    /// configured application becomes active.
    /// Rule format: list of { ExeName (without .exe), PresetSlot 0–4 }.
    /// Rules are serialised to/from a simple JSON array stored in AppSettings.AppPresetMap.
    /// </summary>
    public sealed class AppPresetMap : IDisposable
    {        // ── WinEvent hook ─────────────────────────────────────────────────────
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess,
            uint idThread, uint dwFlags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

        // ── Fields ────────────────────────────────────────────────────────────
        private IntPtr             _hook;
        private WinEventDelegate   _hookDelegate;
        private bool               _disposed;
        private List<AppPresetRule> _rules = new List<AppPresetRule>();

        public event Action<int> PresetRequested;   // slot 0–4

        public class AppPresetRule
        {
            [JsonPropertyName("Exe")]  public string ExeName    { get; set; } = "";  // e.g. "chrome"
            [JsonPropertyName("Slot")] public int    PresetSlot { get; set; } = 0;   // 0–4
        }

        // ── Setup ─────────────────────────────────────────────────────────────
        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hookDelegate = OnWinEvent;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                                    IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        public bool IsRunning => _hook != IntPtr.Zero;

        // ── Rules ─────────────────────────────────────────────────────────────
        public List<AppPresetRule> Rules => _rules;

        public void SetRules(List<AppPresetRule> rules)
            => _rules = rules ?? new List<AppPresetRule>();

        // ── Serialise ─────────────────────────────────────────────────────────
        public string Serialise() => JsonSerializer.Serialize(_rules, MapsJsonContext.Default.ListAppPresetRule);

        public static List<AppPresetRule> Deserialise(string json)
        {
            var list = new List<AppPresetRule>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try { list = JsonSerializer.Deserialize(json, MapsJsonContext.Default.ListAppPresetRule) ?? list; }
            catch (JsonException)
            {
                foreach (Match m in LegacyJson.ObjectRx().Matches(json))
                {
                    string body = m.Groups[1].Value;
                    list.Add(new AppPresetRule { ExeName = LegacyJson.Str(body, "Exe", ""), PresetSlot = LegacyJson.Int(body, "Slot", -1) });
                }
            }
            list.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.ExeName) || r.PresetSlot < 0 || r.PresetSlot > 4);
            return list;
        }

        // ── Hook callback ─────────────────────────────────────────────────────
        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
                                int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (_rules.Count == 0 || hwnd == IntPtr.Zero) return;
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return;
                using (var proc = Process.GetProcessById((int)pid))
                {
                    string exe = proc.ProcessName.ToLowerInvariant();
                    foreach (var rule in _rules)
                    {
                        if (string.Equals(exe,
                            rule.ExeName.ToLowerInvariant().Replace(".exe", ""),
                            StringComparison.OrdinalIgnoreCase))
                        {
                            PresetRequested?.Invoke(rule.PresetSlot);
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
