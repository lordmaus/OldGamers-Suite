using System;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace NoteStation
{
    /// <summary>
    /// A floating sticky note. Each note is its own overlay window.
    /// Per-note font family, size, text color, and background color
    /// are fully independent — this is the primary Low Vision accommodation.
    /// </summary>
    public partial class StickyNote : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int Id { get; set; }

        private string _title = "Note";
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(nameof(Title)); } }
        }

        private string _content = "";
        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    _preview = null;
                    OnPropertyChanged(nameof(Content));
                    OnPropertyChanged(nameof(ContentPreview));
                    OnPropertyChanged(nameof(SafeContentPreview));
                    OnPropertyChanged(nameof(SafeFormatSource));
                }
            }
        }
        public bool   IsVisible   { get; set; } = true;

        /// <summary>When true, this note is reopened on the desktop every time the app starts,
        /// even if it was closed last session. If it has a PIN it opens blurred (locked).</summary>
        public bool   ShowOnStartup { get; set; } = false;

        // ── Position / size ───────────────────────────────────────────────────
        public double Left        { get; set; } = double.NaN;
        public double Top         { get; set; } = double.NaN;
        public double Width       { get; set; } = 300;
        public double Height      { get; set; } = 200;

        // ── Per-note appearance ───────────────────────────────────────────────
        /// <summary>Background color hex e.g. "#FFD700". One of the 8 preset colors.</summary>
        public string BackgroundColor { get; set; } = "#2A2A2A";
        /// <summary>Text/foreground color hex.</summary>
        public string TextColor       { get; set; } = "#FFFFFF";
        /// <summary>Font family name e.g. "Segoe UI", "Arial", "Consolas".</summary>
        public string FontFamily      { get; set; } = "Segoe UI";
        /// <summary>Font size in points.</summary>
        public double FontSize        { get; set; } = 14;
        /// <summary>FontWeight: "Normal" or "Bold".</summary>
        public string FontWeight      { get; set; } = "Normal";
        /// <summary>FontStyle: "Normal" or "Italic".</summary>
        public string FontStyle       { get; set; } = "Normal";

        private double _titleFontSize = 14;
        /// <summary>Title bar font size in points (8–32). Per-note, independent of content font size.</summary>
        public double TitleFontSize
        {
            get => _titleFontSize;
            set { if (_titleFontSize != value) { _titleFontSize = value; OnPropertyChanged(nameof(TitleFontSize)); } }
        }

        // ── Computed (UI only) ────────────────────────────────────────────────
        /// <summary>First 80 chars of content, single line — for list preview. Strips XAML markup.</summary>
        // Cached: the getter ran two regexes per visible list row on every refresh (NS-08).
        private string _preview;
        public string ContentPreview => _preview ??= BuildPreview(Content);

        [GeneratedRegex("<[^>]+>")] private static partial Regex TagRx();
        [GeneratedRegex(@"\s+")] private static partial Regex WsRx();

        private static string BuildPreview(string content)
        {
                var s = content ?? "";
                // Rich text content is XAML — strip tags for plain preview
                if (s.TrimStart().StartsWith("<"))
                {
                    s = TagRx().Replace(s, " ");
                    s = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                         .Replace("&quot;", "\"").Replace("&apos;", "'");
                    s = WsRx().Replace(s, " ").Trim();
                }
                else
                {
                    s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                }
                return s.Length > 80 ? s.Substring(0, 77) + "…" : s;
        }

        // ── Category / folder ─────────────────────────────────────────────────
        public string Category  { get; set; } = "";
        public int?   FolderId  { get; set; }
        public bool   IsPinned   { get; set; }
        /// <summary>When true, a colored tab for this note appears in the docked Notes sidebar (Ctrl+Shift+B).</summary>
        public bool   ShowInSidebar { get; set; } = false;
        public bool   IsTopmost  { get; set; } = true;
        public double NoteOpacity { get; set; } = 1.0;

        /// <summary>PinHasher v2 PBKDF2 hash of the "Hide note" PIN (legacy: SHA-256 hex, upgraded on unlock). Empty = no PIN set.</summary>
        public string PinHash { get; set; } = "";

        private bool _isLocked;
        /// <summary>Runtime only — true while the note is hidden (has a PIN and hasn't been shown this session).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked != value)
                {
                    _isLocked = value;
                    OnPropertyChanged(nameof(IsLocked));
                    OnPropertyChanged(nameof(SafeContentPreview));
                    OnPropertyChanged(nameof(SafeFormatSource));
                }
            }
        }

        /// <summary>ContentPreview, but redacted while the note is locked — never leak body text for a locked note.</summary>
        public string SafeContentPreview => IsLocked ? "Hidden — enter PIN to view" : ContentPreview;

        /// <summary>Raw possibly-XAML rich text source for list-row rendering. Redacted (plain
        /// string, not parsed) while locked — never feed locked body text to the XAML parser either.</summary>
        public string SafeFormatSource => IsLocked ? "Hidden — enter PIN to view" : (Content ?? "");

        /// <summary>Minutes after unlock before note auto-re-blurs. 0 = disabled.</summary>
        public int AutoBlurMinutes { get; set; } = 0;
        /// <summary>If true, this note never auto-blurs even when AutoBlurMinutes > 0.</summary>
        public bool ExcludeFromAutoBlur { get; set; } = false;

        /// <summary>Optional one-time reminder. Null = no reminder. Fires a tray balloon when due.</summary>
        public DateTime? ReminderAt { get; set; } = null;
        /// <summary>True once the reminder balloon has been shown this session. Reset on load.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ReminderFired { get; set; } = false;

        /// <summary>Wildcard window-title pattern to attach this note to (e.g. "*Notepad*"). Empty = not attached.</summary>
        public string AttachedWindowTitle { get; set; } = "";

        /// <summary>Runtime only — true when the note is currently hidden because its attached window is not in focus.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAttachHidden { get; set; } = false;

        /// <summary>Visual texture overlay key. "" = None | "Lined" | "Grid" | "Kraft" | "Cork" | "Dots"</summary>
        public string NoteTexture { get; set; } = "";

        /// <summary>Per-note: hide the entire title bar (title, opacity slider, MD/OCR/close buttons). Right-click restores it. Default false.</summary>
        public bool HideTitleBar { get; set; } = false;

        /// <summary>Folded = only the title bar is shown; content/footer collapsed. Toggled via the fold button next to Close.</summary>
        public bool IsFolded { get; set; } = false;

        // ── Metadata ──────────────────────────────────────────────────────────
        public DateTime CreatedAt  { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
        public bool      IsDeleted  { get; set; } = false;
        public DateTime  DeletedAt  { get; set; }
        /// <summary>Removed from the Recent feed but still kept in its saved folder.</summary>
        public bool      HiddenFromRecent { get; set; } = false;

        // ── Preset colors (8 choices) ─────────────────────────────────────────
        public static readonly string[] PresetBackgrounds = new[]
        {
            "#2A2A2A",   // Dark gray (default dark)
            "#F5F5DC",   // Cream / paper
            "#FFD700",   // Yellow sticky
            "#90EE90",   // Light green
            "#87CEEB",   // Sky blue
            "#FFB6C1",   // Pink
            "#DDA0DD",   // Plum
            "#FFA07A",   // Salmon
        };

        public static readonly string[] PresetTextColors = new[]
        {
            "#FFFFFF",   // White (pairs with dark BG)
            "#1C1C1C",   // Near-black (pairs with light BG)
            "#1C1C1C",
            "#1C1C1C",
            "#1C1C1C",
            "#1C1C1C",
            "#1C1C1C",
            "#1C1C1C",
        };

        // ── Color chip view-model (hex + accessible name) ─────────────────────
        public static readonly NoteColorChip[] PresetChips = new[]
        {
            // Row 1 — darks & neutrals
            new NoteColorChip("#1E1E2E", "#CDD6F4", "Catppuccin dark"),
            new NoteColorChip("#2A2A2A", "#FFFFFF", "Dark gray"),
            new NoteColorChip("#3D3D3D", "#FFFFFF", "Charcoal"),
            new NoteColorChip("#DFE6E9", "#1C1C1C", "Silver"),
            new NoteColorChip("#F5F5DC", "#1C1C1C", "Cream"),
            new NoteColorChip("#FFFFF0", "#1C1C1C", "Ivory"),
            // Row 2 — warm tones
            new NoteColorChip("#FFD700", "#1C1C1C", "Yellow"),
            new NoteColorChip("#FFEAA7", "#1C1C1C", "Pale yellow"),
            new NoteColorChip("#FFA07A", "#1C1C1C", "Salmon"),
            new NoteColorChip("#FF6B6B", "#1C1C1C", "Coral"),
            new NoteColorChip("#E17055", "#FFFFFF", "Terra cotta"),
            new NoteColorChip("#FFB6C1", "#1C1C1C", "Pink"),
            // Row 3 — cool tones
            new NoteColorChip("#90EE90", "#1C1C1C", "Light green"),
            new NoteColorChip("#96CEB4", "#1C1C1C", "Sage"),
            new NoteColorChip("#4ECDC4", "#1C1C1C", "Teal"),
            new NoteColorChip("#87CEEB", "#1C1C1C", "Sky blue"),
            new NoteColorChip("#45B7D1", "#1C1C1C", "Ocean blue"),
            new NoteColorChip("#6C5CE7", "#FFFFFF", "Purple"),
            new NoteColorChip("#DDA0DD", "#1C1C1C", "Plum"),
        };
    }

    /// <summary>Pairs a background hex, text hex, and accessible display name for a color chip.</summary>
    public record NoteColorChip(string Background, string TextColor, string DisplayName);
}
