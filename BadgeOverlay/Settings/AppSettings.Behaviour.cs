using System.Text.Json.Serialization;

namespace BadgeOverlay
{
    /// <summary>
    /// Behaviour and UI-policy flags — controls how the app reacts
    /// rather than what it looks like. Includes detection toggles,
    /// accessibility preferences, startup behaviour, and the InfoOverlay
    /// content settings.
    ///
    /// Split out of the main AppSettings.cs so behavioural-flag
    /// changes don't churn the central serialisation file.
    /// </summary>
    public partial class AppSettings
    {
        // ── Theme / master toggle ─────────────────────────────────────────
        /// <summary>
        /// Named theme key — maps directly to a Themes/*.xaml file.
        /// Old settings.json files that predate this property have no
        /// "ThemeName" key; the default value "Dark" applies on first load.
        /// The previous <c>DarkTheme</c> bool was never serialised (it was
        /// always computed), so no migration of stored data is required.
        /// </summary>
        public string ThemeName { get; set; } = "Dark";

        // ── App-wide font family (Low-Vision) ─────────────────────────────
        /// <summary>
        /// App-wide font family name. Wired into App.xaml's AppFontFamily
        /// resource at runtime by App.SetAppFontFamily(). Low-vision /
        /// dyslexia-friendly picks: "OpenDyslexic", "Atkinson Hyperlegible",
        /// "Verdana". Falls back to "Segoe UI" when null/empty or when the
        /// requested family is not installed.
        /// </summary>
        public string AppFontFamily { get; set; } = "Segoe UI";

        /// <summary>
        /// Badge text size (px). Drives App.xaml's BadgeFontBase resource
        /// so badge TextBlocks bound to it scale together. Clamped to 8..72
        /// on load by AppSettings.Load() to keep stray edits within the
        /// renderable range.
        /// </summary>
        public double BadgeFontSize { get; set; } = 11;

        /// <summary>
        /// Computed from <see cref="ThemeName"/>. True for all dark themes;
        /// false for Light, WarmLight, CatppuccinLatte, GithubLight.
        /// <c>[JsonIgnore]</c> — persisted as ThemeName; this accessor
        /// exists so callers that pre-date the named-theme system continue
        /// to compile without modification.
        /// </summary>
        [JsonIgnore]
        public bool DarkTheme => App.IsThemeDark(ThemeName);

        /// <summary>Master on/off for the badge overlay.</summary>
        public bool OverlayEnabled  { get; set; } = true;

        // ── Accessibility preferences ─────────────────────────────────────

        /// <summary>
        /// WCAG 2.3.3 — when true, the app disables non-essential
        /// animation and interpolation. Specifically: badge position
        /// snaps instead of easing, transitions collapse to instant,
        /// and any fade/slide animations are replaced with immediate
        /// state changes. Honoured in addition to the OS-level
        /// SystemParameters.ClientAreaAnimation preference — either
        /// off disables animation.
        /// </summary>
        public bool ReduceMotion { get; set; } = false;

        /// <summary>
        /// Plays a brief OS system sound when the overlay is toggled
        /// on or off via the global hotkey. Off by default — opt-in
        /// for users who want audio confirmation of state changes
        /// (typically because they can't see the badge state at a
        /// glance: heavy zoom, hidden tray, multi-monitor with the
        /// badge on a different screen). Uses SystemSounds.Asterisk
        /// for "on" and SystemSounds.Hand for "off" so the cue is
        /// distinguishable without requiring custom audio assets.
        /// </summary>
        public bool SoundFeedbackEnabled { get; set; } = false;

        // ── Idle dim ──────────────────────────────────────────────────────
        // After IdleDimSeconds of no cursor movement, the badge fades
        // to IdleDimOpacity to reduce on-screen distraction during
        // stationary tasks (reading, video, dictation). Cursor movement
        // restores full opacity instantly. Detection still runs — only
        // the visual is dimmed.

        /// <summary>Master toggle for idle-dim. Default off so existing users see no behaviour change.</summary>
        public bool   IdleDimEnabled  { get; set; } = false;

        /// <summary>
        /// When true, the app speaks every detected label aloud
        /// through the OS text-to-speech engine (System.Speech /
        /// SAPI). Companion options control when speech fires
        /// (<see cref="SpeechTrigger"/>) and whether the visual badge
        /// stays visible alongside speech
        /// (<see cref="SpeechSuppressesBadge"/>).
        /// </summary>
        public bool ScreenReaderMode { get; set; } = false;

        /// <summary>
        /// Name of the SAPI voice to use for speech, or empty/null
        /// to use the OS default voice. Matched against
        /// <c>InstalledVoice.VoiceInfo.Name</c> — if the saved name is
        /// no longer installed, falls back to the OS default silently.
        /// </summary>
        public string SpeechVoiceName { get; set; } = "";

        /// <summary>
        /// Speech rate: -10 (slowest) to +10 (fastest). 0 = OS default.
        /// The OS user speech-rate setting (Settings → Time &amp; Language
        /// → Speech) applies multiplicatively, so this is a per-app
        /// offset, not an absolute ceiling.
        /// </summary>
        public int SpeechRate { get; set; } = 2;

        /// <summary>
        /// Trigger model for Screen Reader Mode speech.
        /// • Immediate — speak on every label change (legacy behaviour).
        /// • Dwell     — speak when the label has been stable for
        ///               <see cref="SpeechDwellSeconds"/> seconds.
        /// • Hotkey    — never speak automatically; only when the
        ///               user presses the configured Speak hotkey.
        /// </summary>
        public SpeechTriggerMode SpeechTrigger { get; set; } = SpeechTriggerMode.Immediate;

        /// <summary>Dwell threshold in seconds (1–10). Only used when
        /// <see cref="SpeechTrigger"/> is <see cref="SpeechTriggerMode.Dwell"/>.</summary>
        public int SpeechDwellSeconds { get; set; } = 3;

        /// <summary>
        /// When true (the default, preserving legacy behaviour),
        /// enabling Screen Reader Mode also hides the visual badge.
        /// Set false to keep both the visual badge and speech active
        /// at the same time — useful for users with partial vision
        /// who benefit from the badge AND want speech confirmation.
        /// </summary>
        public bool SpeechSuppressesBadge { get; set; } = true;

        /// <summary>Seconds of stationary cursor before fade begins. 1–600.</summary>
        public int    IdleDimSeconds  { get; set; } = 30;

        /// <summary>Target opacity when idle. 0.05–1.0; below 0.05 makes the badge effectively invisible.</summary>
        public double IdleDimOpacity  { get; set; } = 0.30;

        // ── Detection policy ──────────────────────────────────────────────

        /// <summary>
        /// Minimum dwell time (ms) on a recognised element before the
        /// badge label switches to it. Prevents label flicker during
        /// fast cursor movement. 0 = immediate (legacy behaviour).
        /// 100–200 ms feels natural; values above 500 ms feel laggy.
        /// </summary>
        public int HoverDelayMs { get; set; } = 0;

        /// <summary>
        /// When true, the badge is hidden unless the cursor is over the
        /// foreground (focused) application. Useful when working in a
        /// terminal / IDE and you don't want badges over background
        /// chat / email windows.
        /// </summary>
        public bool SuppressWhenBackground { get; set; } = false;

        /// <summary>
        /// When true, the badge is hidden while a fullscreen app is
        /// foreground — games, Zoom/Teams presenter mode, full-screen
        /// video. Detection is structural (foreground window covers the
        /// entire monitor), not driver-level, so it handles both
        /// exclusive-fullscreen and borderless-fullscreen correctly.
        /// </summary>
        public bool PauseOnFullscreen { get; set; } = true;

        /// <summary>
        /// Controls which UIA elements the badge fires on. Trades
        /// badge precision for verbosity — useful for high-density
        /// surfaces like Office ribbons and Visual Studio toolbars
        /// where every element has a name and the badge would
        /// otherwise cascade as the cursor sweeps across.
        /// </summary>
        public DetectionGranularity Granularity { get; set; } = DetectionGranularity.AllNamed;

        // ── UI Automation enhanced detection ──────────────────────────────
        // Enabled by default — essential for browser / Office / WPF
        // button detection. Cursor-dwell gating, shell-region skip, and
        // single-query-per-dwell all work together to keep 3rd-party
        // cursor hooks (YoloMouse) undisturbed while still catching
        // the buttons users hover.
        public bool EnableUIADetection { get; set; } = true;
        public int  UIAThrottleMs      { get; set; } = 150;

        // ── InfoOverlay content ───────────────────────────────────────────
        public bool ShowInfoOverlay     { get; set; } = true;

        /// <summary>
        /// When true, the Info Overlay shows the extended metadata stack
        /// below the label/category — keyboard shortcut, toggle state,
        /// value, link URL, item type. When false, only the headline
        /// label + category render. Lets users opt out of the dense
        /// detail view when it's overwhelming. Default true to preserve
        /// existing behaviour.
        /// </summary>
        public bool ShowInfoOverlayMeta { get; set; } = true;

        // ── Startup ───────────────────────────────────────────────────────
        public bool   StartMinimised   { get; set; } = false;
        public double UiScale          { get; set; } = 1.0;

        /// <summary>
        /// Set to true after the first-run welcome flow (3-screen tour)
        /// has been completed or explicitly dismissed. Used to gate the
        /// welcome window so returning users don't see it on every
        /// launch. Persisted; once true, never auto-flipped back.
        /// </summary>
        public bool FirstRunWelcomed { get; set; } = false;

        // ── Runtime-only state ────────────────────────────────────────────
        // Excluded from JSON — these flags reset every launch.

        /// <summary>
        /// When true, OverlayWindow suppresses label updates — badge
        /// keeps showing the last detected element until toggled off.
        /// Flipped by the freeze hotkey; not persisted (runtime-only).
        /// </summary>
        [JsonIgnore]
        public bool BadgeFrozen { get; set; } = false;
    }
}
