using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReticleOverlay
{
    public partial class HotkeysDialog : Window
    {
        private readonly AppPrefs _prefs;
        private bool _loading = false;
        private DispatcherTimer _savedTimer;
        // Snapshot of hotkey values at open — used by Cancel to roll back live changes.
        private Dictionary<string, string> _snapshot;

        public event EventHandler HotkeysChanged;
        private readonly Func<IReadOnlyDictionary<string, string>> _failures;   // RO-02

        public HotkeysDialog(AppPrefs prefs, Func<IReadOnlyDictionary<string, string>> failures = null)
        {
            _prefs = prefs;
            _failures = failures;
            _loading = true;
            InitializeComponent();
            _loading = false;

            _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _savedTimer.Tick += (s, e) => { LblSaved.Text = ""; _savedTimer.Stop(); };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loading = true;
            TxtHotkeyToggle.Text     = _prefs.HotkeyToggle;
            TxtHotkeyNext.Text       = _prefs.HotkeyProfileNext;
            TxtHotkeyPrev.Text       = _prefs.HotkeyProfilePrev;
            TxtHotkeyOpUp.Text       = _prefs.HotkeyOpacityUp;
            TxtHotkeyOpDown.Text     = _prefs.HotkeyOpacityDown;
            TxtHotkeyNudgeLeft.Text  = _prefs.HotkeyNudgeLeft;
            TxtHotkeyNudgeRight.Text = _prefs.HotkeyNudgeRight;
            TxtHotkeyNudgeUp.Text    = _prefs.HotkeyNudgeUp;
            TxtHotkeyNudgeDown.Text  = _prefs.HotkeyNudgeDown;
            _loading = false;
            CaptureSnapshot();
            RefreshConflicts();
        }

        // RO-02: show which hotkeys Windows refused (owned by a game / another app).
        private TextBox[] Boxes => new[] { TxtHotkeyToggle, TxtHotkeyNext, TxtHotkeyPrev, TxtHotkeyOpUp, TxtHotkeyOpDown,
                                           TxtHotkeyNudgeLeft, TxtHotkeyNudgeRight, TxtHotkeyNudgeUp, TxtHotkeyNudgeDown };

        private void RefreshConflicts()
        {
            var failed = _failures?.Invoke() ?? new Dictionary<string, string>();
            foreach (var tb in Boxes)
            {
                string tag = tb.Tag?.ToString() ?? "";
                if (failed.ContainsKey(tag))
                {
                    tb.SetResourceReference(BorderBrushProperty, "BrushRed");
                    tb.BorderThickness = new Thickness(2);
                    System.Windows.Automation.AutomationProperties.SetItemStatus(tb, "In use by another app");
                }
                else
                {
                    tb.ClearValue(BorderBrushProperty);
                    tb.ClearValue(BorderThicknessProperty);
                    tb.ClearValue(System.Windows.Automation.AutomationProperties.ItemStatusProperty);
                }
            }
            if (failed.Count == 0) { ConflictBanner.Visibility = Visibility.Collapsed; return; }
            ConflictText.Text = "In use by another app: " + string.Join(", ", failed.Values) +
                                ". Pick a different key or close the other app.";
            ConflictBanner.Visibility = Visibility.Visible;
        }

        // Snapshot current prefs so Cancel can roll back live LostFocus commits.
        private void CaptureSnapshot() => _snapshot = new Dictionary<string, string>
        {
            ["Toggle"]     = _prefs.HotkeyToggle,
            ["Next"]       = _prefs.HotkeyProfileNext,
            ["Prev"]       = _prefs.HotkeyProfilePrev,
            ["OpUp"]       = _prefs.HotkeyOpacityUp,
            ["OpDown"]     = _prefs.HotkeyOpacityDown,
            ["NudgeLeft"]  = _prefs.HotkeyNudgeLeft,
            ["NudgeRight"] = _prefs.HotkeyNudgeRight,
            ["NudgeUp"]    = _prefs.HotkeyNudgeUp,
            ["NudgeDown"]  = _prefs.HotkeyNudgeDown,
        };

        private void TxtHotkey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender is not TextBox tb) return;
            string val = tb.Text.Trim();
            bool changed = false;

            switch (tb.Tag?.ToString())
            {
                case "Toggle":     if (_prefs.HotkeyToggle      != val) { _prefs.HotkeyToggle      = val; changed = true; } break;
                case "Next":       if (_prefs.HotkeyProfileNext  != val) { _prefs.HotkeyProfileNext  = val; changed = true; } break;
                case "Prev":       if (_prefs.HotkeyProfilePrev  != val) { _prefs.HotkeyProfilePrev  = val; changed = true; } break;
                case "OpUp":       if (_prefs.HotkeyOpacityUp    != val) { _prefs.HotkeyOpacityUp    = val; changed = true; } break;
                case "OpDown":     if (_prefs.HotkeyOpacityDown  != val) { _prefs.HotkeyOpacityDown  = val; changed = true; } break;
                case "NudgeLeft":  if (_prefs.HotkeyNudgeLeft    != val) { _prefs.HotkeyNudgeLeft    = val; changed = true; } break;
                case "NudgeRight": if (_prefs.HotkeyNudgeRight   != val) { _prefs.HotkeyNudgeRight   = val; changed = true; } break;
                case "NudgeUp":    if (_prefs.HotkeyNudgeUp      != val) { _prefs.HotkeyNudgeUp      = val; changed = true; } break;
                case "NudgeDown":  if (_prefs.HotkeyNudgeDown    != val) { _prefs.HotkeyNudgeDown    = val; changed = true; } break;
            }

            if (changed)
            {
                HotkeysChanged?.Invoke(this, EventArgs.Empty);   // → MainWindow.RegisterHotkeys
                RefreshConflicts();
                bool inUse = _failures?.Invoke()?.ContainsKey(tb.Tag?.ToString() ?? "") == true;
                ShowSaved(inUse ? $"⚠ {val} is in use by another app" : "Hotkey updated ✓");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // Restore every hotkey to its value at dialog open, re-register, then close.
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_snapshot == null) { Close(); return; }
            _loading = true;
            _prefs.HotkeyToggle      = _snapshot["Toggle"];
            _prefs.HotkeyProfileNext = _snapshot["Next"];
            _prefs.HotkeyProfilePrev = _snapshot["Prev"];
            _prefs.HotkeyOpacityUp   = _snapshot["OpUp"];
            _prefs.HotkeyOpacityDown = _snapshot["OpDown"];
            _prefs.HotkeyNudgeLeft   = _snapshot["NudgeLeft"];
            _prefs.HotkeyNudgeRight  = _snapshot["NudgeRight"];
            _prefs.HotkeyNudgeUp     = _snapshot["NudgeUp"];
            _prefs.HotkeyNudgeDown   = _snapshot["NudgeDown"];
            TxtHotkeyToggle.Text     = _prefs.HotkeyToggle;
            TxtHotkeyNext.Text       = _prefs.HotkeyProfileNext;
            TxtHotkeyPrev.Text       = _prefs.HotkeyProfilePrev;
            TxtHotkeyOpUp.Text       = _prefs.HotkeyOpacityUp;
            TxtHotkeyOpDown.Text     = _prefs.HotkeyOpacityDown;
            TxtHotkeyNudgeLeft.Text  = _prefs.HotkeyNudgeLeft;
            TxtHotkeyNudgeRight.Text = _prefs.HotkeyNudgeRight;
            TxtHotkeyNudgeUp.Text    = _prefs.HotkeyNudgeUp;
            TxtHotkeyNudgeDown.Text  = _prefs.HotkeyNudgeDown;
            _loading = false;
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
            Close();
        }

        // RO-08: defaults match a fresh install (F8 / F9 / F10, others blank).
        internal static readonly Dictionary<string, string> Defaults = new()
        {
            ["Toggle"] = "F8", ["Next"] = "F9", ["Prev"] = "F10",
        };

        // RO-08: the button said "Reset All to Defaults" but cleared every key with no confirm.
        // Now confirms (default No) and restores the real defaults; Cancel still reverts to the
        // values at open (same live-commit pattern as LostFocus).
        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this,
                "Reset all hotkeys to defaults?\n\nToggle overlay = F8 · Next profile = F9 · Prev profile = F10 · others blank.\n\n" +
                "Cancel in this dialog still restores what you had when it opened.",
                "Reset hotkeys", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;

            _loading = true;
            _prefs.HotkeyToggle      = Defaults["Toggle"];
            _prefs.HotkeyProfileNext = Defaults["Next"];
            _prefs.HotkeyProfilePrev = Defaults["Prev"];
            _prefs.HotkeyOpacityUp = _prefs.HotkeyOpacityDown =
            _prefs.HotkeyNudgeLeft = _prefs.HotkeyNudgeRight =
            _prefs.HotkeyNudgeUp = _prefs.HotkeyNudgeDown = "";
            TxtHotkeyToggle.Text = _prefs.HotkeyToggle;
            TxtHotkeyNext.Text   = _prefs.HotkeyProfileNext;
            TxtHotkeyPrev.Text   = _prefs.HotkeyProfilePrev;
            TxtHotkeyOpUp.Text = TxtHotkeyOpDown.Text =
            TxtHotkeyNudgeLeft.Text = TxtHotkeyNudgeRight.Text =
            TxtHotkeyNudgeUp.Text = TxtHotkeyNudgeDown.Text = "";
            _loading = false;
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
            RefreshConflicts();
            ShowSaved("Hotkeys reset to defaults ✓");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _savedTimer?.Stop();
            // Prefs are saved by MainWindow on application close.
        }

        private void ShowSaved(string msg)
        {
            LblSaved.Text = msg;
            _savedTimer.Stop();
            _savedTimer.Start();
        }

        // ── Window size persistence ───────────────────────────────────────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var (w, h) = WindowSizeCache.Get("ROHotkeysDialog", 400, 400);
            SizeToContent = System.Windows.SizeToContent.Manual;
            Width = w; Height = h;
        }

        protected override void OnClosed(EventArgs e)
        {
            WindowSizeCache.Set("ROHotkeysDialog", Width, Height);
            base.OnClosed(e);
        }
    }
}
