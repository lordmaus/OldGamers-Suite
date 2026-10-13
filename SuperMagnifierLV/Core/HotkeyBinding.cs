using System;
using System.Collections.Generic;

namespace SuperMagnifier
{
    /// <summary>
    /// Stores a single remappable hotkey: modifiers + virtual key code.
    /// A VirtualKey of 0 means "unbound / disabled".
    /// </summary>
    public sealed class HotkeyBinding
    {
        public uint Modifiers   { get; set; } = GlobalHotkey.MOD_CONTROL;
        public uint VirtualKey  { get; set; } = 0;

        public HotkeyBinding() { }
        public HotkeyBinding(uint modifiers, uint vk)
        {
            Modifiers  = modifiers;
            VirtualKey = vk;
        }

        public bool IsUnbound => VirtualKey == 0;

        /// <summary>Human-readable label, e.g. "Ctrl+M"</summary>
        public string ToDisplayString()
        {
            if (IsUnbound) return "(none)";
            var parts = new List<string>();
            if ((Modifiers & GlobalHotkey.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & GlobalHotkey.MOD_ALT)     != 0) parts.Add("Alt");
            if ((Modifiers & GlobalHotkey.MOD_SHIFT)   != 0) parts.Add("Shift");
            if ((Modifiers & GlobalHotkey.MOD_WIN)     != 0) parts.Add("Win");
            parts.Add(VkToName(VirtualKey));
            return string.Join("+", parts);
        }

        /// <summary>Convert a VK code to a readable key name.</summary>
        public static string VkToName(uint vk) => vk switch
        {
            0x30 => "0",  0x31 => "1",  0x32 => "2",  0x33 => "3",  0x34 => "4",
            0x35 => "5",  0x36 => "6",  0x37 => "7",  0x38 => "8",  0x39 => "9",
            0x41 => "A",  0x42 => "B",  0x43 => "C",  0x44 => "D",  0x45 => "E",
            0x46 => "F",  0x47 => "G",  0x48 => "H",  0x49 => "I",  0x4A => "J",
            0x4B => "K",  0x4C => "L",  0x4D => "M",  0x4E => "N",  0x4F => "O",
            0x50 => "P",  0x51 => "Q",  0x52 => "R",  0x53 => "S",  0x54 => "T",
            0x55 => "U",  0x56 => "V",  0x57 => "W",  0x58 => "X",  0x59 => "Y",
            0x5A => "Z",
            0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
            0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
            0x78 => "F9", 0x79 => "F10",0x7A => "F11",0x7B => "F12",
            0xBB => "=",  0xBD => "-",  0xDB => "[",  0xDD => "]",
            0xBA => ";",  0xBF => "/",  0xDC => "\\", 0xBE => ".",  0xBC => ",",
            0x20 => "Space",
            0x09 => "Tab",
            0x24 => "Home",   0x23 => "End",
            0x21 => "PgUp",   0x22 => "PgDn",
            0x2D => "Insert", 0x2E => "Delete",
            0x0D => "Enter",  0x1B => "Escape",
            0x25 => "←",      0x26 => "↑",      0x27 => "→",      0x28 => "↓",
            _ => $"0x{vk:X2}"
        };

        /// <summary>Current default scheme version (2 = Ctrl+Alt, 2026-10-13).</summary>
        public const int SchemeVersion = 2;

        /// <summary>
        /// Default bindings (scheme 2, SM-12): every global hotkey uses Ctrl+Alt so it no longer
        /// swallows Ctrl+F / Ctrl+T / Ctrl+P / Ctrl+L / Ctrl+R / Ctrl+1–5 etc. in other apps.
        /// Global lens resize is optional (unbound); Ctrl+Shift+Arrow resizes while SML is active.
        /// </summary>
        public static Dictionary<string, HotkeyBinding> Defaults()
        {
            const uint CA  = GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_ALT;
            const uint CAS = CA | GlobalHotkey.MOD_SHIFT;
            var d = new Dictionary<string, HotkeyBinding>
            {
                ["Toggle"]       = new(CA, 0x4D), // Ctrl+Alt+M
                ["Freeze"]       = new(CA, 0x46), // Ctrl+Alt+F
                ["ZoomIn"]       = new(CA, 0xBB), // Ctrl+Alt+=
                ["ZoomOut"]      = new(CA, 0xBD), // Ctrl+Alt+-
                ["ClickThru"]    = new(CA, 0x54), // Ctrl+Alt+T
                ["SnapFocus"]    = new(CA, 0x24), // Ctrl+Alt+Home
                ["Pulse"]        = new(CA, 0x50), // Ctrl+Alt+P
                ["HomeZoom"]     = new(CA, 0x30), // Ctrl+Alt+0
                ["FineToggle"]   = new(CA, 0x47), // Ctrl+Alt+G
                ["OpacityDn"]    = new(CA, 0xDB), // Ctrl+Alt+[
                ["OpacityUp"]    = new(CA, 0xDD), // Ctrl+Alt+]
                ["FollowToggle"] = new(CA, 0x4C), // Ctrl+Alt+L
                ["ReticleToggle"]= new(CA, 0x52), // Ctrl+Alt+R
                ["SizeCycle"]    = new(CA, 0x53), // Ctrl+Alt+S
                ["ColorCycle"]   = new(CA, 0x43), // Ctrl+Alt+C
            };
            for (int i = 0; i < 5; i++)
            {
                d[$"Preset{i + 1}Load"] = new(CA,  (uint)(0x31 + i)); // Ctrl+Alt+1–5
                d[$"Preset{i + 1}Save"] = new(CAS, (uint)(0x31 + i)); // Ctrl+Alt+Shift+1–5
            }
            // Optional global lens resize — unbound by default (SM-03)
            d["ResizeUp"]    = new(CAS, 0);
            d["ResizeDown"]  = new(CAS, 0);
            d["ResizeLeft"]  = new(CAS, 0);
            d["ResizeRight"] = new(CAS, 0);
            return d;
        }

        /// <summary>Scheme-1 defaults (≤ 2026-09-25) — used only to migrate untouched bindings.</summary>
        public static Dictionary<string, HotkeyBinding> DefaultsV1()
        {
            const uint C  = GlobalHotkey.MOD_CONTROL;
            const uint CS = C | GlobalHotkey.MOD_SHIFT;
            var d = new Dictionary<string, HotkeyBinding>
            {
                ["Toggle"] = new(C, 0x4D), ["Freeze"] = new(C, 0x46), ["ZoomIn"] = new(C, 0xBB), ["ZoomOut"] = new(C, 0xBD),
                ["ClickThru"] = new(C, 0x54), ["SnapFocus"] = new(C, 0x24), ["Pulse"] = new(C, 0x50), ["HomeZoom"] = new(C, 0x30),
                ["FineToggle"] = new(C, 0x47), ["OpacityDn"] = new(C, 0xDB), ["OpacityUp"] = new(C, 0xDD),
                ["FollowToggle"] = new(C, 0x4C), ["ReticleToggle"] = new(C, 0x52),
                ["SizeCycle"] = new(C | GlobalHotkey.MOD_ALT, 0x53), ["ColorCycle"] = new(CS, 0x43),
            };
            for (int i = 0; i < 5; i++)
            {
                d[$"Preset{i + 1}Load"] = new(C,  (uint)(0x31 + i));
                d[$"Preset{i + 1}Save"] = new(CS, (uint)(0x31 + i));
            }
            return d;
        }

        /// <summary>True for Ctrl / Ctrl+Shift + key with no Alt/Win — the chords other apps rely on.</summary>
        public bool IsBareCtrlChord =>
            !IsUnbound && (Modifiers & (GlobalHotkey.MOD_ALT | GlobalHotkey.MOD_WIN)) == 0;

        public bool SameAs(HotkeyBinding o) => o != null && o.Modifiers == Modifiers && o.VirtualKey == VirtualKey;

        /// <summary>Human-readable action names for the remap dialog.</summary>
        public static Dictionary<string, string> ActionLabels() =>
            new()
            {
                ["Toggle"]     = "Toggle Magnifier on / off",
                ["Freeze"]     = "Freeze / unfreeze lens",
                ["ZoomIn"]     = "Zoom in one step",
                ["ZoomOut"]    = "Zoom out one step",
                ["ClickThru"]  = "Toggle Click-through Mode",
                ["SnapFocus"]  = "Snap lens to cursor",
                ["Pulse"]      = "Cursor Finder Pulse",
                ["HomeZoom"]   = "Return to Home Zoom",
                ["FineToggle"] = "Toggle Fine zoom mode",
                ["OpacityDn"]  = "Decrease lens opacity",
                ["OpacityUp"]  = "Increase lens opacity",
                ["FollowToggle"] = "Toggle Follow Mode (Mouse ↔ Caret)",
                ["ReticleToggle"]= "Toggle Reticle overlay",
                ["SizeCycle"]    = "Cycle lens size (default → 2× → default)",
                ["ColorCycle"]   = "Cycle colour mode",
                ["ResizeUp"]     = "Resize lens shorter (any app)",
                ["ResizeDown"]   = "Resize lens taller (any app)",
                ["ResizeLeft"]   = "Resize lens narrower (any app)",
                ["ResizeRight"]  = "Resize lens wider (any app)",
                // FIX #5: Preset hotkeys shown read-only in remap dialog
                ["Preset1Load"] = "Load Preset 1",
                ["Preset2Load"] = "Load Preset 2",
                ["Preset3Load"] = "Load Preset 3",
                ["Preset4Load"] = "Load Preset 4",
                ["Preset5Load"] = "Load Preset 5",
                ["Preset1Save"] = "Save Preset 1",
                ["Preset2Save"] = "Save Preset 2",
                ["Preset3Save"] = "Save Preset 3",
                ["Preset4Save"] = "Save Preset 4",
                ["Preset5Save"] = "Save Preset 5",
            };
    }
}
