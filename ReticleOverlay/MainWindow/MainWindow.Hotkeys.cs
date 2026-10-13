using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace ReticleOverlay
{
    public partial class MainWindow
    {
        // ── Registration ──────────────────────────────────────────────────────
        // RO-02: tag (HotkeysDialog TextBox.Tag) → "Label (key)" for every hotkey Windows refused,
        // usually because a game or another app already owns it. Read by HotkeysDialog.
        private readonly Dictionary<string, string> _hotkeyFailures = new();
        internal IReadOnlyDictionary<string, string> HotkeyFailures => _hotkeyFailures;

        private void RegisterHotkeys()
        {
            _hotkeys.UnregisterAll();
            _hotkeyFailures.Clear();
            void Try(string key, string tag, string label, Action cb)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                if (_hotkeys.Register(key, cb) < 0)
                    _hotkeyFailures[tag] = $"{label} ({key})";
            }
            Try(_prefs.HotkeyToggle,      "Toggle",     "Toggle overlay", () => Dispatcher.Invoke(ToggleReticle));
            Try(_prefs.HotkeyProfileNext, "Next",       "Next profile",   () => Dispatcher.Invoke(CycleProfileNext));
            Try(_prefs.HotkeyProfilePrev, "Prev",       "Prev profile",   () => Dispatcher.Invoke(CycleProfilePrev));
            Try(_prefs.HotkeyOpacityUp,   "OpUp",       "Opacity +",      () => Dispatcher.Invoke(NudgeOpacityUp));
            Try(_prefs.HotkeyOpacityDown, "OpDown",     "Opacity −",      () => Dispatcher.Invoke(NudgeOpacityDown));
            Try(_prefs.HotkeyNudgeLeft,   "NudgeLeft",  "Nudge Left",     () => Dispatcher.Invoke(() => NudgeOffset(-1, 0)));
            Try(_prefs.HotkeyNudgeRight,  "NudgeRight", "Nudge Right",    () => Dispatcher.Invoke(() => NudgeOffset(1, 0)));
            Try(_prefs.HotkeyNudgeUp,     "NudgeUp",    "Nudge Up",       () => Dispatcher.Invoke(() => NudgeOffset(0, -1)));
            Try(_prefs.HotkeyNudgeDown,   "NudgeDown",  "Nudge Down",     () => Dispatcher.Invoke(() => NudgeOffset(0, 1)));
            if (_hotkeyFailures.Count > 0)
                ShowSaved("⚠ Hotkey in use by another app — see Hotkeys dialog");
        }

        // ── Actions ───────────────────────────────────────────────────────────
        private void ToggleReticle()
        {
            _prefs.ReticleVisible = !_prefs.ReticleVisible;
            UpdateBtnToggle();
            _tray?.UpdateToggleLabel(_prefs.ReticleVisible);
            UpdateTrayTooltip();
            MarkDirty();
            RefreshOverlay();
        }

        private void CycleProfileNext()
        {
            if (_profiles.Count == 0) return;
            int idx = (CmbProfiles.SelectedIndex + 1) % _profiles.Count;
            CmbProfiles.SelectedIndex = idx;
            ApplyProfile(_profiles[idx]);
        }

        private void CycleProfilePrev()
        {
            if (_profiles.Count == 0) return;
            int idx = (CmbProfiles.SelectedIndex - 1 + _profiles.Count) % _profiles.Count;
            CmbProfiles.SelectedIndex = idx;
            ApplyProfile(_profiles[idx]);
        }

        private void NudgeOpacityUp()
        {
            _prefs.Opacity = Math.Round(_prefs.Opacity + 0.05, 2);
            using (_suppress.Enter()) { SldrOpacity.Value = _prefs.Opacity; LblOpacity.Text = ((int)(_prefs.Opacity * 100)) + "%"; }
        }

        private void NudgeOpacityDown()
        {
            _prefs.Opacity = Math.Round(_prefs.Opacity - 0.05, 2);
            using (_suppress.Enter()) { SldrOpacity.Value = _prefs.Opacity; LblOpacity.Text = ((int)(_prefs.Opacity * 100)) + "%"; }
        }

        private void NudgeOffset(double dx, double dy)
        {
            _prefs.OffsetX += dx;
            _prefs.OffsetY += dy;
            using (_suppress.Enter())
            {
                SldrOffsetX.Value = _prefs.OffsetX; LblOffsetX.Text = ((int)_prefs.OffsetX) + " px";
                SldrOffsetY.Value = _prefs.OffsetY; LblOffsetY.Text = ((int)_prefs.OffsetY) + " px";
            }
        }
    }
}
