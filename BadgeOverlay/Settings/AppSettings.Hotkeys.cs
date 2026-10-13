namespace BadgeOverlay
{
    /// <summary>
    /// Global hotkey bindings — virtual-key codes, modifier masks,
    /// and per-binding enable flags. Nine bindings:
    ///   • Toggle overlay      (default Ctrl+Shift+O)
    ///   • Freeze label        (default Ctrl+Shift+F, disabled by default)
    ///   • Copy label          (default Ctrl+Shift+C, disabled by default)
    ///   • Toggle speech       (default Ctrl+Shift+S, disabled by default)
    ///   • Load Preset 1-5     (default Ctrl+1-5,       disabled by default)
    ///   • Save to Preset 1-5  (default Ctrl+Shift+1-5, disabled by default)
    ///
    /// Binding edits round-trip through HotkeysWindow with a pre-flight
    /// conflict probe (RegisterHotKey returning false at probe time
    /// surfaces a warning before the user dismisses the dialog).
    ///
    /// Modifier values are the raw Win32 RegisterHotKey flags:
    ///   MOD_ALT=0x0001, MOD_CONTROL=0x0002, MOD_SHIFT=0x0004, MOD_WIN=0x0008
    /// </summary>
    public partial class AppSettings
    {
        // ── Toggle overlay ────────────────────────────────────────────────
        public uint HotkeyToggleVK      { get; set; } = 0x4F;   // 'O'
        public uint HotkeyToggleMods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeyToggleEnabled { get; set; } = true;

        // ── Freeze badge ──────────────────────────────────────────────────
        // Pauses badge label updates while held/toggled. Useful for
        // screen-reader users who want to read the current label
        // without having the cursor re-point at something else
        // mid-read.
        public uint HotkeyFreezeVK      { get; set; } = 0x46;   // 'F'
        public uint HotkeyFreezeMods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeyFreezeEnabled { get; set; } = false;

        // ── Copy label ────────────────────────────────────────────────────
        // Puts the currently-detected label text on the clipboard.
        // Provides an accessible alternative to selecting text directly
        // on the badge (which is click-through to preserve the
        // motor-safety guarantee that the overlay never intercepts clicks).
        public uint HotkeyCopyLabelVK      { get; set; } = 0x43;   // 'C'
        public uint HotkeyCopyLabelMods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeyCopyLabelEnabled { get; set; } = false;

        // ── Toggle speech on/off ──────────────────────────────────────────
        // Runtime mute switch over the persisted Screen Reader Mode
        // preference. Pressing the hotkey toggles whether speech is
        // currently audible without changing the configured trigger
        // mode (Immediate / Dwell) or dwell time. Audio confirmation
        // (Asterisk on, Hand off) on every toggle.
        // Default Ctrl+Shift+S; off until the user enables it (so the
        // chord doesn't conflict with apps that bind Ctrl+Shift+S to
        // "Save As" out of the box).
        public uint HotkeySpeakVK      { get; set; } = 0x53;   // 'S'
        public uint HotkeySpeakMods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeySpeakEnabled { get; set; } = false;

        // ── Load Preset 1-5 ──────────────────────────────────────────────────
        // Recall a saved preset slot by keyboard. Default chord is Ctrl+1-5
        // but all five are disabled by default — Ctrl+1-5 collides with
        // browser tab navigation and many text editors. Users opt in via
        // the Hotkeys settings window.
        public uint HotkeyLoadPreset1VK      { get; set; } = 0x31;   // '1'
        public uint HotkeyLoadPreset1Mods    { get; set; } = 0x0002; // Ctrl
        public bool HotkeyLoadPreset1Enabled { get; set; } = false;

        public uint HotkeyLoadPreset2VK      { get; set; } = 0x32;   // '2'
        public uint HotkeyLoadPreset2Mods    { get; set; } = 0x0002; // Ctrl
        public bool HotkeyLoadPreset2Enabled { get; set; } = false;

        public uint HotkeyLoadPreset3VK      { get; set; } = 0x33;   // '3'
        public uint HotkeyLoadPreset3Mods    { get; set; } = 0x0002; // Ctrl
        public bool HotkeyLoadPreset3Enabled { get; set; } = false;

        public uint HotkeyLoadPreset4VK      { get; set; } = 0x34;   // '4'
        public uint HotkeyLoadPreset4Mods    { get; set; } = 0x0002; // Ctrl
        public bool HotkeyLoadPreset4Enabled { get; set; } = false;

        public uint HotkeyLoadPreset5VK      { get; set; } = 0x35;   // '5'
        public uint HotkeyLoadPreset5Mods    { get; set; } = 0x0002; // Ctrl
        public bool HotkeyLoadPreset5Enabled { get; set; } = false;

        // ── Save to Preset 1-5 ───────────────────────────────────────────────
        // Capture current settings into a preset slot by keyboard.
        // Default Ctrl+Shift+1-5; disabled by default for same reason as
        // load presets. Ctrl+Shift+1-5 conflicts with many IDEs.
        public uint HotkeySavePreset1VK      { get; set; } = 0x31;   // '1'
        public uint HotkeySavePreset1Mods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeySavePreset1Enabled { get; set; } = false;

        public uint HotkeySavePreset2VK      { get; set; } = 0x32;   // '2'
        public uint HotkeySavePreset2Mods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeySavePreset2Enabled { get; set; } = false;

        public uint HotkeySavePreset3VK      { get; set; } = 0x33;   // '3'
        public uint HotkeySavePreset3Mods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeySavePreset3Enabled { get; set; } = false;

        public uint HotkeySavePreset4VK      { get; set; } = 0x34;   // '4'
        public uint HotkeySavePreset4Mods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeySavePreset4Enabled { get; set; } = false;

        public uint HotkeySavePreset5VK      { get; set; } = 0x35;   // '5'
        public uint HotkeySavePreset5Mods    { get; set; } = 0x0006; // Ctrl+Shift
        public bool HotkeySavePreset5Enabled { get; set; } = false;

        // ── Factory-default reset ─────────────────────────────────────────────
        /// <summary>
        /// Copies all hotkey properties from a fresh <see cref="AppSettings"/>
        /// instance, restoring factory defaults. Called by
        /// HotkeysWindow "Reset All to Defaults" so the button actually resets
        /// instead of re-reading the already-modified settings object.
        /// </summary>
        public void ResetHotkeyDefaults()
        {
            var d = new AppSettings();
            HotkeyToggleVK      = d.HotkeyToggleVK;      HotkeyToggleMods    = d.HotkeyToggleMods;    HotkeyToggleEnabled    = d.HotkeyToggleEnabled;
            HotkeyFreezeVK      = d.HotkeyFreezeVK;      HotkeyFreezeMods    = d.HotkeyFreezeMods;    HotkeyFreezeEnabled    = d.HotkeyFreezeEnabled;
            HotkeyCopyLabelVK   = d.HotkeyCopyLabelVK;   HotkeyCopyLabelMods = d.HotkeyCopyLabelMods; HotkeyCopyLabelEnabled = d.HotkeyCopyLabelEnabled;
            HotkeySpeakVK       = d.HotkeySpeakVK;       HotkeySpeakMods     = d.HotkeySpeakMods;     HotkeySpeakEnabled     = d.HotkeySpeakEnabled;

            HotkeyLoadPreset1VK = d.HotkeyLoadPreset1VK; HotkeyLoadPreset1Mods = d.HotkeyLoadPreset1Mods; HotkeyLoadPreset1Enabled = d.HotkeyLoadPreset1Enabled;
            HotkeyLoadPreset2VK = d.HotkeyLoadPreset2VK; HotkeyLoadPreset2Mods = d.HotkeyLoadPreset2Mods; HotkeyLoadPreset2Enabled = d.HotkeyLoadPreset2Enabled;
            HotkeyLoadPreset3VK = d.HotkeyLoadPreset3VK; HotkeyLoadPreset3Mods = d.HotkeyLoadPreset3Mods; HotkeyLoadPreset3Enabled = d.HotkeyLoadPreset3Enabled;
            HotkeyLoadPreset4VK = d.HotkeyLoadPreset4VK; HotkeyLoadPreset4Mods = d.HotkeyLoadPreset4Mods; HotkeyLoadPreset4Enabled = d.HotkeyLoadPreset4Enabled;
            HotkeyLoadPreset5VK = d.HotkeyLoadPreset5VK; HotkeyLoadPreset5Mods = d.HotkeyLoadPreset5Mods; HotkeyLoadPreset5Enabled = d.HotkeyLoadPreset5Enabled;

            HotkeySavePreset1VK = d.HotkeySavePreset1VK; HotkeySavePreset1Mods = d.HotkeySavePreset1Mods; HotkeySavePreset1Enabled = d.HotkeySavePreset1Enabled;
            HotkeySavePreset2VK = d.HotkeySavePreset2VK; HotkeySavePreset2Mods = d.HotkeySavePreset2Mods; HotkeySavePreset2Enabled = d.HotkeySavePreset2Enabled;
            HotkeySavePreset3VK = d.HotkeySavePreset3VK; HotkeySavePreset3Mods = d.HotkeySavePreset3Mods; HotkeySavePreset3Enabled = d.HotkeySavePreset3Enabled;
            HotkeySavePreset4VK = d.HotkeySavePreset4VK; HotkeySavePreset4Mods = d.HotkeySavePreset4Mods; HotkeySavePreset4Enabled = d.HotkeySavePreset4Enabled;
            HotkeySavePreset5VK = d.HotkeySavePreset5VK; HotkeySavePreset5Mods = d.HotkeySavePreset5Mods; HotkeySavePreset5Enabled = d.HotkeySavePreset5Enabled;
        }
    }
}
