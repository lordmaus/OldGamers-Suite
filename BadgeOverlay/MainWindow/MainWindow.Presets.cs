using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace BadgeOverlay
{
    /// <summary>
    /// Section 5 — Preset 1-5 / Save logic for MainWindow.
    ///
    /// Interaction model:
    ///   • Plain click on a slot → recall (load) that preset into live settings.
    ///     If slot is empty the click is a no-op (button still focuses normally).
    ///   • Ctrl+Click on any slot → capture current settings into that slot.
    ///     This is the same as selecting the slot then pressing Save.
    ///   • Save button → save current settings to the active (highlighted) slot.
    ///     Button is disabled until a slot is selected.
    ///
    /// Three visual states (set by UpdatePresetButtons via Style assignment):
    ///   PresetButton        — empty slot      (muted text, no ◆)
    ///   PresetButtonSaved   — slot has data   (full text, ◆ dot visible)
    ///   PresetButtonActive  — selected slot   (blue bg, white text)
    ///
    /// WCAG compliance:
    ///   1.4.1  Non-color: ◆ glyph conveys saved state; active state uses
    ///          blue bg + white text AND bold weight (not color-only).
    ///   2.5.5  44px min-height on all buttons (set in PresetButton styles).
    ///   4.1.2  AutomationProperties.Name updated each call with slot number,
    ///          state (empty/saved/active), and preset name if set.
    /// </summary>
    public partial class MainWindow
    {
        // ── State ────────────────────────────────────────────────────────────────

        private Button[]   _presetBtns;
        private TextBlock[] _presetDots;

        // ── Init ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once from OnLoaded after InitLandmarks().
        /// Populates the button and dot arrays from named XAML elements so
        /// UpdatePresetButtons() doesn't need to walk the visual tree each call.
        /// </summary>
        private void InitPresetButtons()
        {
            _presetBtns = new[]
            {
                BtnPreset1, BtnPreset2, BtnPreset3, BtnPreset4, BtnPreset5,
            };
            _presetDots = new[]
            {
                PresetDot1, PresetDot2, PresetDot3, PresetDot4, PresetDot5,
            };
        }

        // ── View update ──────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes all five preset buttons to reflect the current state of
        /// <see cref="_s.Presets"/> and <see cref="_s.ActivePresetIndex"/>.
        /// Safe to call before InitPresetButtons() — null-guards throughout.
        /// </summary>
        private void UpdatePresetButtons()
        {
            if (_presetBtns == null || _s?.Presets == null) return;

            int active = _s.ActivePresetIndex;

            for (int i = 0; i < _presetBtns.Length; i++)
            {
                var btn = _presetBtns[i];
                if (btn == null) continue;

                bool isActive = (i == active);
                bool hasSaved = i < _s.Presets.Length
                                && _s.Presets[i] != null
                                && !_s.Presets[i].IsEmpty;

                // ── Style ────────────────────────────────────────────────────────
                string styleKey = isActive   ? "PresetButtonActive"
                                : hasSaved   ? "PresetButtonSaved"
                                             : "PresetButton";
                btn.Style = (Style)FindResource(styleKey);

                // ── ◆ dot visibility ─────────────────────────────────────────────
                var dot = i < _presetDots.Length ? _presetDots[i] : null;
                if (dot != null)
                    dot.Visibility = hasSaved ? Visibility.Visible : Visibility.Collapsed;

                // ── AutomationProperties.Name ────────────────────────────────────
                string name  = _s.Presets[i]?.Name ?? "";
                string label = string.IsNullOrWhiteSpace(name)
                               ? $"Preset {i + 1}"
                               : $"Preset {i + 1}: {name}";

                string state = isActive ? "active"
                             : hasSaved ? "saved"
                                        : "empty";

                string hint = isActive ? "Press Save to overwrite, or click another preset."
                            : hasSaved ? "Click to recall. Ctrl+Click to overwrite."
                                       : "Ctrl+Click or press Save after selecting to save here.";

                AutomationProperties.SetName(btn, $"{label}, {state}");
                AutomationProperties.SetHelpText(btn, hint);
            }

            // Save button — enabled only when a slot is selected.
            if (BtnPresetSave != null)
                BtnPresetSave.IsEnabled = (active >= 0 && active < 5);
        }

        // ── Click handlers ───────────────────────────────────────────────────────

        /// <summary>
        /// Handles clicks on preset slot buttons (BtnPreset1 … BtnPreset5).
        ///
        /// Plain click — recall preset (no-op when empty).
        /// Ctrl+Click  — save current settings to this slot (always succeeds).
        /// </summary>
        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!int.TryParse(btn.Tag?.ToString(), out int index)) return;
            if ((uint)index >= 5u) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl)
            {
                // Ctrl+Click → save
                _s.SaveToPreset(index);
                PushSettings();
                UpdatePresetButtons();
            }
            else
            {
                // Plain click → select slot; recall if non-empty
                _s.ActivePresetIndex = index;

                if (_s.LoadFromPreset(index))
                {
                    // Preset applied — refresh all controls to match new settings.
                    _loading = true;
                    try   { LoadSettingsIntoControls(); }
                    finally { _loading = false; }

                    _overlay?.ApplySettings(_s);
                    PushSettings();
                }
                else
                {
                    // Empty slot — just highlight it so Save works.
                    UpdatePresetButtons();
                }
            }
        }

        /// <summary>
        /// Saves current settings into the active preset slot.
        /// Button is disabled (IsEnabled=False) when no slot is selected,
        /// so this handler can assume ActivePresetIndex is valid.
        /// </summary>
        private void BtnPresetSave_Click(object sender, RoutedEventArgs e)
        {
            int index = _s.ActivePresetIndex;
            if ((uint)index >= 5u) return;

            _s.SaveToPreset(index);
            PushSettings();
            UpdatePresetButtons();
        }

        // ── Hotkey-callable entry points ─────────────────────────────────────────
        // Called from MainWindow.Hotkeys.cs via Dispatcher.Invoke — already on
        // the UI thread when these execute.

        /// <summary>
        /// Recalls preset at <paramref name="index"/> (0-based). Selects the slot,
        /// applies settings if non-empty, and refreshes controls. No-op for
        /// empty slots — the slot is still highlighted so a subsequent Save works.
        /// </summary>
        internal void RecallPreset(int index)
        {
            if ((uint)index >= 5u) return;
            _s.ActivePresetIndex = index;

            if (_s.LoadFromPreset(index))
            {
                _loading = true;
                try   { LoadSettingsIntoControls(); }
                finally { _loading = false; }
                _overlay?.ApplySettings(_s);
                PushSettings();
            }
            else
            {
                // Empty slot — highlight for Save without changing settings.
                UpdatePresetButtons();
            }
        }

        /// <summary>
        /// Saves current settings into the slot at <paramref name="index"/> (0-based),
        /// selecting it as the active slot in the process.
        /// </summary>
        internal void SavePreset(int index)
        {
            if ((uint)index >= 5u) return;
            _s.ActivePresetIndex = index;
            _s.SaveToPreset(index);
            PushSettings();
            UpdatePresetButtons();
        }
    }
}
