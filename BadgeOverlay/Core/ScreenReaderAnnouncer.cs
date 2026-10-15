using System;
using System.Linq;
using System.Speech.Synthesis;

namespace BadgeOverlay
{
    /// <summary>
    /// Direct text-to-speech announcer for Screen Reader Mode.
    ///
    /// <para>Why TTS instead of UIA live regions:</para>
    /// The original implementation used an off-screen WPF window with
    /// <c>AutomationProperties.LiveSetting=Polite</c>. Reliable
    /// reproduction of that approach across NVDA / JAWS / Narrator
    /// turned out to require the announcer window be focusable and on-
    /// screen — both unacceptable for a passive cursor-following badge.
    /// SAPI is the simpler, more deterministic mechanism: speaks the
    /// label directly through the OS speech engine, works whether or
    /// not a screen reader is configured / running, and doesn't
    /// require WPF UIA peer realisation games.
    ///
    /// <para>Threading.</para>
    /// <see cref="SpeechSynthesizer.SpeakAsync"/> is non-blocking.
    /// All Speak calls go through a single synthesizer instance owned
    /// by this class; SAPI internally serialises requests on its own
    /// thread. We wrap each call in try/catch because SAPI can throw
    /// when the OS audio device is unavailable (no speakers, RDP
    /// session redirecting audio, system muted at the driver level).
    ///
    /// <para>Throttling.</para>
    /// Same-label suppression — identical consecutive labels don't
    /// re-announce. We also call <see cref="SpeechSynthesizer.SpeakAsyncCancelAll"/>
    /// before queueing a new label so fast cursor movement doesn't
    /// build a backlog of pending phrases (cursor passing over
    /// "File / Edit / View / Window / Help" in 200 ms must NOT
    /// produce 30 seconds of buffered speech).
    ///
    /// <para>Lifecycle.</para>
    /// Lazy-initialised on first <see cref="Announce"/>. Safe to call
    /// <see cref="Dispose"/> from any thread; subsequent Announce
    /// calls become no-ops rather than throwing.
    /// </summary>
    internal sealed class ScreenReaderAnnouncer : IDisposable
    {
        private SpeechSynthesizer _synth;
        private string _lastAnnounced;
        private bool _disposed;
        private readonly object _lock = new object();

        /// <summary>
        /// Non-null when the SAPI synthesizer failed to initialise.
        /// Surfaces the failure to the UI so the user gets a clear
        /// message rather than silent SR Mode. Typical causes:
        ///   • Windows N/KN editions (speech not installed)
        ///   • Server Core or Server without Desktop Experience
        ///   • No audio output device configured
        ///   • Restricted execution environment (sandboxed app)
        /// Callers should check this after construction and before
        /// the user first attempts to use SR Mode.
        /// </summary>
        public string InitError { get; private set; }

        /// <summary>True when SAPI initialised successfully.</summary>
        public bool IsAvailable => _synth != null;

        public ScreenReaderAnnouncer()
        {
            try
            {
                _synth = new SpeechSynthesizer();
                _synth.SetOutputToDefaultAudioDevice();
                _synth.Rate = 2;   // default until ApplySettings is called
            }
            catch (Exception ex)
            {
                AppLog.Error("Announcer ctor", ex);
                InitError = BuildDiagnostic(ex);
                _synth = null;
            }
        }

        /// <summary>
        /// Applies voice name and rate from <paramref name="settings"/>.
        /// Safe to call repeatedly as settings change. Falls back to
        /// OS default voice silently when the saved name is not found
        /// (e.g. voice uninstalled after settings were saved).
        /// </summary>
        public void ApplySettings(AppSettings settings)
        {
            if (settings == null || _synth == null) return;
            lock (_lock)
            {
                try
                {
                    // Rate first — cheap, always succeeds.
                    _synth.Rate = Math.Max(-10, Math.Min(10, settings.SpeechRate));

                    // Voice — match by name, fall back to OS default.
                    string wanted = settings.SpeechVoiceName?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(wanted))
                    {
                        var voices = _synth.GetInstalledVoices();
                        var match = voices.FirstOrDefault(v =>
                            v.Enabled &&
                            string.Equals(v.VoiceInfo.Name, wanted,
                                          StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                            _synth.SelectVoice(match.VoiceInfo.Name);
                        // else: leave current voice; saves are only applied
                        // when user explicitly selects, so a "not found"
                        // state means the voice was uninstalled.
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Announcer.ApplySettings", ex);
                }
            }
        }

        /// <summary>
        /// Returns display names of all enabled installed voices, in
        /// the order SAPI reports them. Called by the MainWindow voice
        /// picker ComboBox; empty list means SAPI unavailable.
        /// </summary>
        public static System.Collections.Generic.List<string> GetInstalledVoiceNames()
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                using var probe = new SpeechSynthesizer();
                foreach (var v in probe.GetInstalledVoices())
                    if (v.Enabled) result.Add(v.VoiceInfo.Name);
            }
            catch { /* swallow — SAPI not available */ }
            return result;
        }

        /// <summary>
        /// Builds a user-readable diagnostic for a SAPI init failure,
        /// with a concrete remediation step where possible. The raw
        /// exception message alone is too cryptic for most users.
        /// </summary>
        private static string BuildDiagnostic(Exception ex)
        {
            string msg = ex?.Message ?? "(unknown error)";

            // Common known patterns — ordered most-specific first.
            if (msg.Contains("No audio") || msg.Contains("audio device") ||
                ex is System.ComponentModel.Win32Exception w32 && w32.NativeErrorCode == 0x47F)
                return "No audio output device found. " +
                       "Connect speakers or headphones and try again.";

            if (ex is System.Runtime.InteropServices.COMException com)
            {
                // SPERR_NOT_FOUND = 0x80045003 — speech runtime not installed
                if ((uint)com.HResult == 0x80045003)
                    return "Windows Speech Runtime is not installed on this system. " +
                           "On Windows N/KN editions, install the Media Feature Pack from " +
                           "Settings → Apps → Optional features.";

                // SAPI 5 not registered (rare, usually on Server Core)
                if ((uint)com.HResult == 0x80040154)
                    return "Speech API (SAPI 5) is not registered. " +
                           "This can occur on Windows Server Core or minimal installs. " +
                           "Install the Desktop Experience feature role.";
            }

            // Fallback with full message for unknown errors.
            return $"Speech engine failed to start: {msg}\n" +
                   "Check Settings → Time & Language → Speech and ensure " +
                   "a voice is installed and audio output is available.";
        }

        /// <summary>
        /// Speaks a label. Same-label suppression prevents
        /// over-speaking; null/empty is a silent no-op. Cancels any
        /// in-flight or pending speech before queueing the new label
        /// so we don't build a backlog during fast cursor movement.
        /// Safe to call from the UI thread; SAPI is async internally.
        /// </summary>
        /// <param name="label">Text to speak.</param>
        /// <param name="force">When true, bypasses the same-label
        /// suppression — speaks even if the label matches the previous
        /// announcement. Used by the "Speak now" hotkey so repeated
        /// presses re-announce the current label.</param>
        public void Announce(string label, bool force = false)
        {
            if (_disposed) return;
            string normalised = string.IsNullOrWhiteSpace(label) ? "" : label.Trim();
            if (normalised.Length == 0) return;

            lock (_lock)
            {
                if (!force && normalised == _lastAnnounced) return;
                _lastAnnounced = normalised;

                if (_synth == null) return;

                try
                {
                    // Cancel any prior in-flight or queued phrase so we
                    // don't build up a backlog when the cursor sweeps
                    // across many controls in quick succession.
                    _synth.SpeakAsyncCancelAll();
                    _synth.SpeakAsync(normalised);
                }
                catch (Exception ex) { AppLog.Error("Announcer Speak", ex); }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _synth?.SpeakAsyncCancelAll();
                    _synth?.Dispose();
                }
                catch { /* swallow on shutdown */ }
                _synth = null;
            }
        }

        /// <summary>
        /// Cancels any in-flight or queued speech without disposing.
        /// Used by the runtime mute toggle so muting takes effect
        /// immediately even mid-utterance, without tearing down the
        /// synthesizer (it'll be needed again the moment the user
        /// unmutes).
        /// </summary>
        public void CancelInFlight()
        {
            lock (_lock)
            {
                if (_disposed) return;
                try { _synth?.SpeakAsyncCancelAll(); }
                catch { /* swallow */ }
                // Reset same-label suppression so the next Announce
                // after unmute fires even if it's the same label that
                // was just mid-utterance.
                _lastAnnounced = null;
            }
        }
    }
}
