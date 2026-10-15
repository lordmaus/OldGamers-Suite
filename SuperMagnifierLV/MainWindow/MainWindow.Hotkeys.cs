using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace SuperMagnifier
{
    // Hotkey registration/disposal extracted into a partial class for maintainability.
    // All fields referenced here are declared in MainWindow.xaml.cs.
    public partial class MainWindow
    {
        // ── Hotkeys ───────────────────────────────────────────────────────────
        private void RegisterHotkeys()
        {
            DisposeHotkeys();
            if (!_settings.HotkeyToggle) return;
            var failed = new System.Collections.Generic.List<string>();
            try
            {
                var src = PresentationSource.FromVisual(this) as HwndSource;
                if (src == null) return;

                // Use saved bindings, falling back to defaults for any missing key
                var bindings = _settings.HotkeyBindings ?? HotkeyBinding.Defaults();
                HotkeyBinding B(string key) =>
                    bindings.TryGetValue(key, out var b) && !b.IsUnbound ? b
                    : (HotkeyBinding.Defaults().TryGetValue(key, out var d) ? d : null);

                var labels = HotkeyBinding.ActionLabels();
                GlobalHotkey Make(int id, string key, Action action)
                {
                    var b = B(key);
                    if (b == null || b.IsUnbound) return null;
                    var hk = new GlobalHotkey(src, id, b.Modifiers, b.VirtualKey);
                    if (!hk.IsRegistered)
                        failed.Add($"{(labels.TryGetValue(key, out var lbl) ? lbl : key)} ({b.ToDisplayString()})");
                    // WM_HOTKEY is delivered through our HwndSource hook on the UI thread — no
                    // Dispatcher.Invoke needed (SM-08).
                    hk.Pressed += (s, _) => action();
                    return hk;
                }

                _hotkeyToggle    = Make(1,  "Toggle",     Toggle);
                _hotkeyFreeze    = Make(2,  "Freeze",     () => _lens.ToggleFreeze());
                _hotkeyZoomIn    = Make(3,  "ZoomIn",     () => ZoomBy(+_zoomStep));
                _hotkeyZoomOut   = Make(4,  "ZoomOut",    () => ZoomBy(-_zoomStep));
                _hotkeyClickThru = Make(5,  "ClickThru",  () => { _lens.SetClickThrough(!_lens.IsClickThrough); UpdateClickThruCheck(); });
                _hotkeySnapFocus = Make(16, "SnapFocus",  () => _engine.ResetSmoothPosition());
                _hotkeyPulse     = Make(17, "Pulse",      () => _cursorPulse?.Fire());
                _hotkeyHomeZoom  = Make(18, "HomeZoom",   () => { SetZoom(_settings.HomeZoom); ShowTrayBalloon($"Home zoom: {_settings.HomeZoom:F1}×", ms: 1200); });
                _hotkeyFineToggle= Make(19, "FineToggle", () => { if (_zoomStep == 0.1) SetStepRadio(_settings.Step > 0.1 ? _settings.Step : 0.25); else SetStepRadio(0.1); });
                _hotkeyOpacityDn = Make(20, "OpacityDn",   () => AdjustOpacity(-0.1));
                _hotkeyOpacityUp = Make(21, "OpacityUp",   () => AdjustOpacity(+0.1));
                _hotkeyColorCycle    = Make(22, "ColorCycle",    CycleColorMode);
                _hotkeyFollowToggle  = Make(23, "FollowToggle",  () => ToggleFollowMode());
                _hotkeyReticleToggle = Make(24, "ReticleToggle", () => ToggleReticle());
                _hotkeySizeCycle     = Make(25, "SizeCycle",     () => CycleLensSize());

                // Preset load/save — remappable since scheme 2 (default Ctrl+Alt+1–5 / Ctrl+Alt+Shift+1–5)
                _hotkeyPresetLoad = new GlobalHotkey[5];
                _hotkeyPresetSave = new GlobalHotkey[5];
                for (int i = 0; i < 5; i++)
                {
                    int slot = i;
                    _hotkeyPresetLoad[slot] = Make(6  + slot, $"Preset{slot + 1}Load", () => LoadPreset(slot));
                    _hotkeyPresetSave[slot] = Make(11 + slot, $"Preset{slot + 1}Save", () => SavePreset(slot));
                }

                // Optional global lens resize (unbound by default — SM-03)
                _hotkeyResizeUp    = Make(30, "ResizeUp",    () => ResizeLens(0, -20));
                _hotkeyResizeDn    = Make(31, "ResizeDown",  () => ResizeLens(0, +20));
                _hotkeyResizeLeft  = Make(32, "ResizeLeft",  () => ResizeLens(-20, 0));
                _hotkeyResizeRight = Make(33, "ResizeRight", () => ResizeLens(+20, 0));
            }
            catch (System.Exception _ex) { AppLog.Error("RegisterHotkeys", _ex); }

            UpdateHotkeyHints();

            // Notify user of any registration conflicts
            if (failed.Count > 0)
                Dispatcher.BeginInvoke(new Action(() =>
                    ShowToast($"⚠ Hotkey conflict: {string.Join(", ", failed)}", ms: 4000)));
        }

        /// <summary>Display text for an action's current binding, e.g. "Ctrl+Alt+F" or "(none)".</summary>
        private string Chord(string key) =>
            (_settings?.HotkeyBindings != null && _settings.HotkeyBindings.TryGetValue(key, out var b) ? b
             : HotkeyBinding.Defaults()[key]).ToDisplayString();

        /// <summary>Tooltips / headings that name a hotkey read the live binding (no hard-coded "Ctrl+F").</summary>
        private void UpdateHotkeyHints()
        {
            try
            {
                string freeze = Chord("Freeze"), zin = Chord("ZoomIn"), zout = Chord("ZoomOut");
                FreezeBtn.ToolTip  = $"Freeze lens ({freeze})";
                System.Windows.Automation.AutomationProperties.SetHelpText(FreezeBtn,
                    $"Freeze or unfreeze the magnified view. Keyboard shortcut: {freeze}");
                ZoomDownBtn.ToolTip = $"Zoom out ({zout})";
                ZoomUpBtn.ToolTip   = $"Zoom in ({zin})";
                HeadFollowMode.Text    = $"Follow Mode  ({Chord("FollowToggle")})";
                HeadFollowMode.ToolTip = $"{Chord("FollowToggle")} toggles between Follow Mouse and Follow Text Caret";
                HeadReticle.Text       = $"Reticle  ({Chord("ReticleToggle")})";
                HeadReticle.ToolTip    = $"{Chord("ReticleToggle")} toggles the reticle overlay on and off";
                HeadHomeZoom.Text      = $"Home Zoom  ({Chord("HomeZoom")})";
                HeadHomeZoom.ToolTip   = $"Press {Chord("HomeZoom")} to instantly return to this zoom level";
                PresetHelpText.Text    = $"Click=load  ·  Then click Save to overwrite  ·  {Chord("Preset1Load")}…  /  {Chord("Preset1Save")}…";
                _lens?.SetHotkeyHints(freeze, zin, zout);
            }
            catch (System.Exception ex) { AppLog.Error("UpdateHotkeyHints", ex); }
        }

        private void DisposeHotkeys()
        {
            _hotkeyToggle?.Dispose();    _hotkeyToggle    = null;
            _hotkeyFreeze?.Dispose();    _hotkeyFreeze    = null;
            _hotkeyZoomIn?.Dispose();    _hotkeyZoomIn    = null;
            _hotkeyZoomOut?.Dispose();   _hotkeyZoomOut   = null;
            _hotkeyClickThru?.Dispose(); _hotkeyClickThru = null;
            _hotkeySnapFocus?.Dispose();   _hotkeySnapFocus   = null;
            _hotkeyPulse?.Dispose();       _hotkeyPulse       = null;
            _hotkeyHomeZoom?.Dispose();    _hotkeyHomeZoom    = null;
            _hotkeyFineToggle?.Dispose();  _hotkeyFineToggle  = null;
            _hotkeyOpacityDn?.Dispose();   _hotkeyOpacityDn   = null;
            _hotkeyOpacityUp?.Dispose();   _hotkeyOpacityUp   = null;
            _hotkeyColorCycle?.Dispose();  _hotkeyColorCycle  = null;
            _hotkeyFollowToggle?.Dispose();  _hotkeyFollowToggle  = null;
            _hotkeyReticleToggle?.Dispose(); _hotkeyReticleToggle = null;
            _hotkeySizeCycle?.Dispose();     _hotkeySizeCycle     = null;
            if (_hotkeyPresetLoad != null)
                for (int i = 0; i < _hotkeyPresetLoad.Length; i++)
                { _hotkeyPresetLoad[i]?.Dispose(); _hotkeyPresetLoad[i] = null; }
            if (_hotkeyPresetSave != null)
                for (int i = 0; i < _hotkeyPresetSave.Length; i++)
                { _hotkeyPresetSave[i]?.Dispose(); _hotkeyPresetSave[i] = null; }
            _hotkeyResizeUp?.Dispose();    _hotkeyResizeUp    = null;
            _hotkeyResizeDn?.Dispose();    _hotkeyResizeDn    = null;
            _hotkeyResizeLeft?.Dispose();  _hotkeyResizeLeft  = null;
            _hotkeyResizeRight?.Dispose(); _hotkeyResizeRight = null;
        }
    }
}
