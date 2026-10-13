using System;
using System.Windows.Interop;

namespace BadgeOverlay
{
    /// <summary>
    /// Global-hotkey registration and audio-cue helpers for
    /// <see cref="MainWindow"/>. The three hotkey fields
    /// (_hotkeyToggle, _hotkeyFreeze, _hotkeyCopyLabel) live on the
    /// main partial because they're disposed in
    /// <see cref="MainWindow.DisposeRuntimeResources"/>; keeping the
    /// declarations there avoids forcing two partials to coordinate
    /// dispose order.
    ///
    /// Re-registration on settings change goes through
    /// <see cref="RegisterHotkeys"/>; first-run registration is wired
    /// from <see cref="MainWindow.OnLoaded"/>.
    /// </summary>
    public partial class MainWindow
    {
        // ── Hotkeys ───────────────────────────────────────────────────────────
        private void RegisterHotkeys()
        {
            _hotkeyToggle?.Dispose();    _hotkeyToggle    = null;
            _hotkeyFreeze?.Dispose();    _hotkeyFreeze    = null;
            _hotkeyCopyLabel?.Dispose(); _hotkeyCopyLabel = null;
            _hotkeySpeak?.Dispose();     _hotkeySpeak     = null;
            for (int i = 0; i < 5; i++)
            {
                _hotkeyLoadPresets[i]?.Dispose(); _hotkeyLoadPresets[i] = null;
                _hotkeySavePresets[i]?.Dispose(); _hotkeySavePresets[i] = null;
            }

            try
            {
                var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (src == null) return;

                _hotkeyToggle = TryRegister(src, "Toggle",
                    _s.HotkeyToggleEnabled, _s.HotkeyToggleMods, _s.HotkeyToggleVK,
                    () =>
                    {
                        // Toggle through the ViewModel so the TwoWay binding
                        // propagates to TglEnable and OnViewModelPropertyChanged
                        // handles the _s sync + PushSettings + UpdateStatusBar.
                        bool next = !(_vm?.OverlayEnabled ?? _s.OverlayEnabled);
                        if (_vm != null)
                            _vm.OverlayEnabled = next;
                        else
                        {
                            _s.OverlayEnabled = next;
                            PushSettings();
                            UpdateStatusBar();
                        }
                        PlayStateSound(next);
                    });

                _hotkeyFreeze = TryRegister(src, "Freeze",
                    _s.HotkeyFreezeEnabled, _s.HotkeyFreezeMods, _s.HotkeyFreezeVK,
                    () =>
                    {
                        _s.BadgeFrozen = !_s.BadgeFrozen;
                        UpdateStatusBar();
                        PlayStateSound(_s.BadgeFrozen);
                    });

                _hotkeyCopyLabel = TryRegister(src, "Copy label",
                    _s.HotkeyCopyLabelEnabled, _s.HotkeyCopyLabelMods, _s.HotkeyCopyLabelVK,
                    () =>
                    {
                        try
                        {
                            string label = _overlay?.LastLabel ?? "";
                            if (!string.IsNullOrEmpty(label))
                                System.Windows.Clipboard.SetText(label);
                        }
                        catch (Exception ex) { CrashLog.Write("CopyLabelHotkey", ex); }
                    });

                // Toggle speech on / off — runtime mute switch over
                // the persisted ScreenReaderMode preference. Useful
                // for muting during meetings / phone calls without
                // losing the configured trigger mode and dwell.
                _hotkeySpeak = TryRegister(src, "Toggle speech",
                    _s.HotkeySpeakEnabled, _s.HotkeySpeakMods, _s.HotkeySpeakVK,
                    () =>
                    {
                        try { _overlay?.ToggleSpeechMuted(); }
                        catch (Exception ex) { CrashLog.Write("ToggleSpeechHotkey", ex); }
                    });

                // Load Preset 1-5 — recall saved settings from a slot.
                // Parallel arrays keep the loop body short without needing
                // reflection or a switch block.
                uint[]  loadVK      = { _s.HotkeyLoadPreset1VK,   _s.HotkeyLoadPreset2VK,   _s.HotkeyLoadPreset3VK,   _s.HotkeyLoadPreset4VK,   _s.HotkeyLoadPreset5VK   };
                uint[]  loadMods    = { _s.HotkeyLoadPreset1Mods,  _s.HotkeyLoadPreset2Mods,  _s.HotkeyLoadPreset3Mods,  _s.HotkeyLoadPreset4Mods,  _s.HotkeyLoadPreset5Mods  };
                bool[]  loadEnabled = { _s.HotkeyLoadPreset1Enabled, _s.HotkeyLoadPreset2Enabled, _s.HotkeyLoadPreset3Enabled, _s.HotkeyLoadPreset4Enabled, _s.HotkeyLoadPreset5Enabled };

                for (int i = 0; i < 5; i++)
                {
                    int slot = i;   // capture for lambda
                    _hotkeyLoadPresets[slot] = TryRegister(src, $"Load Preset {slot + 1}",
                        loadEnabled[slot], loadMods[slot], loadVK[slot],
                        () => RecallPreset(slot));
                }

                // Save to Preset 1-5 — capture current settings into a slot.
                uint[]  saveVK      = { _s.HotkeySavePreset1VK,   _s.HotkeySavePreset2VK,   _s.HotkeySavePreset3VK,   _s.HotkeySavePreset4VK,   _s.HotkeySavePreset5VK   };
                uint[]  saveMods    = { _s.HotkeySavePreset1Mods,  _s.HotkeySavePreset2Mods,  _s.HotkeySavePreset3Mods,  _s.HotkeySavePreset4Mods,  _s.HotkeySavePreset5Mods  };
                bool[]  saveEnabled = { _s.HotkeySavePreset1Enabled, _s.HotkeySavePreset2Enabled, _s.HotkeySavePreset3Enabled, _s.HotkeySavePreset4Enabled, _s.HotkeySavePreset5Enabled };

                for (int i = 0; i < 5; i++)
                {
                    int slot = i;
                    _hotkeySavePresets[slot] = TryRegister(src, $"Save Preset {slot + 1}",
                        saveEnabled[slot], saveMods[slot], saveVK[slot],
                        () => SavePreset(slot));
                }
            }
            catch (Exception ex) { CrashLog.Write("RegisterHotkeys", ex); }
        }

        /// <summary>
        /// Registers one global hotkey, logging conflicts and wiring the
        /// supplied UI-thread action. Returns null when the hotkey is
        /// disabled in settings, has VK=0, or fails to register (another
        /// app already owns the chord). Caller assigns the returned
        /// instance to its field for later disposal.
        /// </summary>
        private GlobalHotkey TryRegister(HwndSource src, string name,
            bool enabled, uint mods, uint vk, Action onPressed)
        {
            if (!enabled || vk == 0) return null;

            var hk = new GlobalHotkey(src, mods, vk);
            if (!hk.IsRegistered)
            {
                hk.Dispose();
                CrashLog.Write(
                    $"Hotkey '{name}' failed to register " +
                    $"(mods={mods:X} vk={vk:X}). " +
                    "Another app is probably using the same shortcut.");
                return null;
            }
            hk.Pressed += (s, e) => Dispatcher.Invoke(onPressed);
            return hk;
        }

        /// <summary>
        /// Plays a brief OS system sound to confirm a state toggle —
        /// <see cref="System.Media.SystemSounds.Asterisk"/> for "now
        /// on" and <see cref="System.Media.SystemSounds.Hand"/> for
        /// "now off". Distinguishable cues without shipping custom
        /// audio assets. No-op when <see cref="AppSettings.SoundFeedbackEnabled"/>
        /// is false (default). Failures are silent — sound is a UX
        /// nicety, not a requirement; an exception here must not
        /// disrupt the toggle action that triggered it.
        /// </summary>
        private void PlayStateSound(bool nowOn)
        {
            if (_s == null || !_s.SoundFeedbackEnabled) return;
            try
            {
                if (nowOn) System.Media.SystemSounds.Asterisk.Play();
                else       System.Media.SystemSounds.Hand.Play();
            }
            catch { /* swallow: audio unavailable / muted / no device */ }
        }
    }
}
