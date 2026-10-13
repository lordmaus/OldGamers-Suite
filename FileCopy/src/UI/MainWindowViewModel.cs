using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

#nullable enable


namespace FileCopy
{
    /// <summary>
    /// Central VM for MainWindow. Owns observable application state and delegates
    /// preference persistence to <see cref="AppPrefs"/>. Forwards prefs property
    /// changes so bindings to either VM or AppPrefs see live updates.
    /// </summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        public ObservableRangeCollection<FileItem> Items   { get; } = new ObservableRangeCollection<FileItem>();
        public ObservableCollection<FileItem>      Errors  { get; } = new ObservableCollection<FileItem>();
        public ObservableCollection<PresetSlot>    Presets { get; } = new ObservableCollection<PresetSlot>();
        public AppPrefs Prefs => AppPrefs.Instance;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // ── Status / runtime ─────────────────────────────────────────────────
        public string StatusText { get => field; set { if (field == value) return; field = value; Notify(); } } = "Idle";
        public string SpeedText  { get => field; set { if (field == value) return; field = value; Notify(); } } = "";
        public string SummaryText { get => field; set { if (field == value) return; field = value; Notify(); } } = "No scan yet.";
        public bool   IsRunning  { get => field; set { if (field == value) return; field = value; Notify(); Notify(nameof(IsIdle)); Notify(nameof(FileProgressHelp)); Notify(nameof(TotalProgressHelp)); CommandManager.InvalidateRequerySuggested(); } }
        public bool   IsIdle => !IsRunning;

        // ── Progress (percent + label) ───────────────────────────────────────
        public double FileProgressPct  { get => field; set { if (field == value) return; field = value; Notify(); Notify(nameof(FileProgressText)); Notify(nameof(FileProgressHelp)); } }
        public double TotalProgressPct { get => field; set { if (field == value) return; field = value; Notify(); Notify(nameof(TotalProgressText)); Notify(nameof(TotalProgressHelp)); } }
        public string FileProgressText  => FileProgressPct.ToString("0") + "%";
        public string TotalProgressText => TotalProgressPct.ToString("0") + "%";
        public string FileProgressHelp  => IsRunning ? $"Current file: {FileProgressText}" : "Not running";
        public string TotalProgressHelp => IsRunning ? $"Overall: {TotalProgressText}" : "Not running";

        // ── Pause display ────────────────────────────────────────────────────
        public string PauseLabel { get => field; set { if (field == value) return; field = value; Notify(); Notify(nameof(PauseIcon)); } } = "Pause";
        /// <summary>JtIcon key for the Pause/Resume button (Resume reuses the Start play icon).</summary>
        public string PauseIcon => PauseLabel == "Resume" ? "queueNow" : "pause";

        // ── UI scale display ─────────────────────────────────────────────────
        public string UiScaleLabel { get => field; set { if (field == value) return; field = value; Notify(); } } = "⊞ Standard (1.0×)";

        // ── Theme picker ─────────────────────────────────────────────────────
        public System.Collections.Generic.IReadOnlyList<AppTheme.ThemeInfo> AvailableThemes
            => AppTheme.AllThemes;

        public AppTheme.ThemeInfo SelectedTheme
        {
            get => AppTheme.AllThemes.FirstOrDefault(t => t.Key == AppPrefs.Instance.Theme)
                   ?? AppTheme.AllThemes[0];
            set
            {
                if (value == null || value.Key == AppPrefs.Instance.Theme) return;
                AppPrefs.Instance.Theme = value.Key;
                App.ForceApplyTheme();
                Notify();
            }
        }

        // ── Error panel ──────────────────────────────────────────────────────
        public string ErrorCountText  => Errors.Count == 1 ? "1 error" : $"{Errors.Count} errors";
        public bool   HasErrors       => Errors.Count > 0;
        public bool   HasNoItems      => Items.Count == 0;

        public MainWindowViewModel()
        {
            // Forward AppPrefs changes so XAML bindings to {Binding Prefs.X} work.
            Prefs.PropertyChanged += (s, e) => Notify(nameof(Prefs));

            // Build preset slots from prefs
            for (int i = 0; i < 5; i++)
                Presets.Add(new PresetSlot(i + 1));
            RefreshPresetSlots();
            Prefs.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppPrefs.Presets)) RefreshPresetSlots();
            };

            Errors.CollectionChanged += (_, __) =>
            {
                Notify(nameof(ErrorCountText));
                Notify(nameof(HasErrors));
            };
            Items.CollectionChanged += (_, __) => Notify(nameof(HasNoItems));
        }

        public void RefreshPresetSlots()
        {
            var arr = Prefs.Presets;
            for (int i = 0; i < Presets.Count; i++)
            {
                bool has = i < arr.Length && arr[i] is not null;
                Presets[i].HasData = has;
            }
        }
    }

    /// <summary>One preset button row in the bottom bar.</summary>
    public sealed class PresetSlot : INotifyPropertyChanged
    {
        public int Slot { get; }
        public string SlotText => Slot.ToString();
        public bool HasData { get => field; set { if (field == value) return; field = value; Notify(nameof(HasData)); Notify(nameof(Display)); Notify(nameof(Tooltip)); Notify(nameof(StatusDescription)); } }
        public string Display           => HasData ? $"● {Slot}" : Slot.ToString();
        public string Tooltip           => HasData
            ? $"Preset {Slot} (saved) — click to load  (Ctrl+{Slot})"
            : $"Preset {Slot} (empty) — click to save  (Ctrl+Shift+{Slot})";
        public string StatusDescription => HasData
            ? $"Slot {Slot}: saved preset. Press to load, or Ctrl+Shift+{Slot} to overwrite."
            : $"Slot {Slot}: empty. Press Ctrl+Shift+{Slot} to save current settings here.";

        public PresetSlot(int slot) { Slot = slot; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string? n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
