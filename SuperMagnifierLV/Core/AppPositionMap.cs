using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SuperMagnifier
{
    /// <summary>
    /// Stores and restores the lens position when specific applications become active.
    /// A position entry is automatically created/updated when the user moves the lens
    /// while a tracked application is in the foreground.
    /// Each entry stores the monitor-relative position so it survives monitor layout changes.
    /// </summary>
    public sealed class AppPositionMap : IDisposable
    {
        // ── WinEvent hook (reused from AppPresetMap pattern) ──────────────────
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess,
            uint idThread, uint dwFlags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(
            IntPtr hWnd, out uint lpdwProcessId);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

        // ── Position record ───────────────────────────────────────────────────
        public class AppPositionEntry
        {
            [JsonPropertyName("Exe")] public string ExeName { get; set; } = "";
            [JsonPropertyName("Mon")] public string MonName { get; set; } = "";
            public double RelX     { get; set; } = 0.05;
            public double RelY     { get; set; } = 0.05;
        }

        // ── Fields ────────────────────────────────────────────────────────────
        private IntPtr           _hook;
        private WinEventDelegate _hookDelegate;
        private bool             _disposed;
        private string           _currentExe = "";

        private readonly Dictionary<string, AppPositionEntry> _map =
            new Dictionary<string, AppPositionEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fires when a tracked app comes to the foreground — carry lens to its saved position.</summary>
        public event Action<AppPositionEntry> PositionRequested;

        // ── Start / Stop ──────────────────────────────────────────────────────
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

        // ── Map access ────────────────────────────────────────────────────────
        public IReadOnlyDictionary<string, AppPositionEntry> Entries => _map;

        public void SetEntries(IEnumerable<AppPositionEntry> entries)
        {
            _map.Clear();
            foreach (var e in entries)
                if (!string.IsNullOrWhiteSpace(e.ExeName))
                    _map[e.ExeName.ToLowerInvariant()] = e;
        }

        /// <summary>
        /// Save the current lens position for the exe that is currently in the foreground.
        /// Call this whenever the lens is moved/resized.
        /// </summary>
        public void SaveCurrentPosition(double logLeft, double logTop, float dpiScale)
        {
            if (string.IsNullOrEmpty(_currentExe)) return;
            var (monName, relX, relY) = MonitorHelper.GetRelativePosition(logLeft, logTop, dpiScale);
            _map[_currentExe] = new AppPositionEntry
            {
                ExeName = _currentExe,
                MonName = monName,
                RelX    = relX,
                RelY    = relY,
            };
        }

        // ── Serialise ─────────────────────────────────────────────────────────
        public string Serialise() =>
            JsonSerializer.Serialize(_map.Values.ToList(), MapsJsonContext.Default.ListAppPositionEntry);

        public static List<AppPositionEntry> Deserialise(string json)
        {
            var list = new List<AppPositionEntry>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try { list = JsonSerializer.Deserialize(json, MapsJsonContext.Default.ListAppPositionEntry) ?? list; }
            catch (JsonException)
            {
                // Legacy: RelX/RelY may have been written as "0,0500" on comma-decimal locales (SM-01).
                foreach (Match m in LegacyJson.ObjectRx().Matches(json))
                {
                    string body = m.Groups[1].Value;
                    list.Add(new AppPositionEntry
                    {
                        ExeName = LegacyJson.Str(body, "Exe", ""),
                        MonName = LegacyJson.Str(body, "Mon", ""),
                        RelX    = LegacyJson.Dbl(body, "RelX", 0.05),
                        RelY    = LegacyJson.Dbl(body, "RelY", 0.05),
                    });
                }
            }
            list.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.ExeName));
            return list;
        }

        // ── Hook callback ─────────────────────────────────────────────────────
        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
                                int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return;
                using (var proc = Process.GetProcessById((int)pid))
                {
                    string exe = proc.ProcessName.ToLowerInvariant();
                    _currentExe = exe;
                    if (_map.TryGetValue(exe, out var entry))
                        PositionRequested?.Invoke(entry);
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
