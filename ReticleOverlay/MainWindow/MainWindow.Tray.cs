namespace ReticleOverlay
{
    public partial class MainWindow
    {
        // ── Tray icon state ───────────────────────────────────────────────────
        private void UpdateTrayTooltip()
        {
            string name = CmbProfiles.SelectedIndex >= 0 && CmbProfiles.SelectedIndex < _profiles.Count
                ? _profiles[CmbProfiles.SelectedIndex].Name : "";
            _tray?.UpdateTooltip(name, _prefs.ReticleVisible);
        }

        // Option B (2026-09-25 icon pass): fixed Overlay icon; the words, the button style and the
        // UIA Name / ToolTip carry the on/off state (never the icon alone).
        private void UpdateBtnToggle()
        {
            bool on = _prefs.ReticleVisible;
            string hk = string.IsNullOrWhiteSpace(_prefs.HotkeyToggle) ? "" : $" ({_prefs.HotkeyToggle})";
            BtnToggleText.Text = on ? "Overlay on" : "Overlay off";
            BtnToggle.Style    = (System.Windows.Style)FindResource(on ? "PrimaryButton" : "FlatButton");
            BtnToggle.ToolTip  = on ? $"Reticle overlay is on — click to hide{hk}" : $"Reticle overlay is off — click to show{hk}";
            System.Windows.Automation.AutomationProperties.SetName(BtnToggle, on ? "Reticle overlay: on" : "Reticle overlay: off");
        }
    }
}
