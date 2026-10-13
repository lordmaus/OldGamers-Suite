using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BadgeOverlay.ViewModels
{
    /// <summary>
    /// Foundation ViewModel for MainWindow.
    ///
    /// MVVM migration status: the app currently uses code-behind (LoadSettingsIntoControls /
    /// PushSettings pattern). This file establishes the INotifyPropertyChanged
    /// infrastructure so properties can migrate here incrementally without a
    /// big-bang rewrite.
    ///
    /// Migration path:
    ///   1. Add a property here with the field keyword + OnPropertyChanged.
    ///   2. Set DataContext = new MainWindowViewModel(_s) in OnLoaded.
    ///   3. Bind the XAML control to the ViewModel property.
    ///   4. Remove the corresponding manual assignment from LoadSettingsIntoControls
    ///      and the corresponding read from the changed handler.
    ///
    /// Priority candidates (most-read/written in LoadSettingsIntoControls):
    ///   OverlayEnabled, ShowBadge, BadgeOpacity, BadgeScale,
    ///   BadgeOffsetX, HoverDelayMs — done. UiScale is NOT a simple bind:
    ///   it drives a discrete preset index (UiScales[]/UiScaleLabels[] in
    ///   MainWindow.xaml.cs), a LayoutTransform, AND a composed button
    ///   label ("⊞ {Name} ({Scale}×)") rather than a plain slider value —
    ///   migrating it means moving that cycling/lookup logic into the VM
    ///   too, not just adding a bound property. Left in code-behind.
    ///
    /// C# 14 field keyword: use in any property setter that has validation /
    /// side-effect logic — the implicit backing field replaces the private _field
    /// declaration entirely. Example:
    ///
    ///   public double BadgeOpacity
    ///   {
    ///       get;
    ///       set
    ///       {
    ///           if (field == value) return;
    ///           field = value < 0.30 ? 0.30 : value > 1.0 ? 1.0 : value;
    ///           OnPropertyChanged();
    ///       }
    ///   } = 0.95;
    /// </summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Overlay master toggle ─────────────────────────────────────────────
        // Migrate: bind TglEnable.IsChecked here, remove from LoadSettingsIntoControls.
        public bool OverlayEnabled
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                OnPropertyChanged();
            }
        }

        // ── Badge visibility ──────────────────────────────────────────────────
        public bool ShowBadge
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                OnPropertyChanged();
            }
        } = true;

        // ── Badge opacity [0.30–1.0] ──────────────────────────────────────────
        public double BadgeOpacity
        {
            get;
            set
            {
                double clamped = value < 0.30 ? 0.30 : value > 1.0 ? 1.0 : value;
                if (field == clamped) return;
                field = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BadgeOpacityLabel));
            }
        } = 0.95;

        /// <summary>Formatted string for the opacity label (e.g. "95 %").</summary>
        public string BadgeOpacityLabel
            => $"{(int)(BadgeOpacity * 100)} %";

        // ── Badge scale [0.5–2.0] ─────────────────────────────────────────────
        public double BadgeScale
        {
            get;
            set
            {
                double clamped = value < 0.5 ? 0.5 : value > 2.0 ? 2.0 : value;
                if (field == clamped) return;
                field = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BadgeScaleLabel));
            }
        } = 1.0;

        public string BadgeScaleLabel => $"{BadgeScale:F1}×";

        // ── Badge distance from cursor, px [4-80] ──────────────────────────────
        // Drives BOTH _s.BadgeOffsetX and _s.BadgeOffsetY in the settings bridge
        // (OnViewModelPropertyChanged) — MainWindow has always used a single
        // slider for both axes, this property just carries that name forward.
        public double BadgeOffsetX
        {
            get;
            set
            {
                double clamped = value < 4 ? 4 : value > 80 ? 80 : value;
                if (field == clamped) return;
                field = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BadgeOffsetLabel));
            }
        } = 20;

        public string BadgeOffsetLabel => $"{(int)BadgeOffsetX} px";

        // ── Hover delay before the badge label switches targets, ms [0-500] ────
        public int HoverDelayMs
        {
            get;
            set
            {
                int clamped = value < 0 ? 0 : value > 500 ? 500 : value;
                if (field == clamped) return;
                field = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HoverDelayLabel));
            }
        }

        public string HoverDelayLabel => HoverDelayMs == 0 ? "instant" : $"{HoverDelayMs} ms";

        // ── Constructor ───────────────────────────────────────────────────────
        public MainWindowViewModel() { }

        /// <summary>
        /// Initialise from a live settings snapshot. Called from OnLoaded once
        /// _s is available. Does NOT wire save-back — that remains in code-behind
        /// until each property's changed handler is migrated.
        /// </summary>
        public MainWindowViewModel(AppSettings s)
        {
            OverlayEnabled = s.OverlayEnabled;
            ShowBadge      = s.ShowBadge;
            BadgeOpacity   = s.BadgeOpacity;
            BadgeScale     = s.BadgeScale > 0 ? s.BadgeScale : 1.0;
            BadgeOffsetX   = s.BadgeOffsetX;
            HoverDelayMs   = s.HoverDelayMs;
        }
    }
}
