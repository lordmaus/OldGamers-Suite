using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace NoteStation
{
    /// <summary>One entry in the clipboard history ring.</summary>
    public class ClipboardItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int    Id        { get; set; }

        private string _title = "";
        /// <summary>User-assigned label (does not affect clip content).</summary>
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string Text      { get; set; } = "";

        private string _preview = "";
        /// <summary>First 120 chars, single-line.</summary>
        public string Preview
        {
            get => _preview;
            set { if (_preview != value) { _preview = value; OnPropertyChanged(); } }
        }

        private DateTime _capturedAt = DateTime.Now;
        public DateTime CapturedAt
        {
            get => _capturedAt;
            set { if (_capturedAt != value) { _capturedAt = value; OnPropertyChanged(); } }
        }

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
        }

        public string Tag       { get; set; } = "";   // optional user label
        public string Category  { get; set; } = "";
        public int?   FolderId  { get; set; }
        /// <summary>Window title of the app that was in foreground when this clip was captured.</summary>
        public string SourceApp { get; set; } = "";
        public bool      IsDeleted  { get; set; } = false;
        public DateTime  DeletedAt  { get; set; }
        /// <summary>Removed from the Recent feed but still kept in its saved folder.</summary>
        public bool      HiddenFromRecent { get; set; } = false;

        /// <summary>Remembered size of the Edit Clip window the last time this clip was edited. Null = use default.</summary>
        public double? EditWidth  { get; set; }
        public double? EditHeight { get; set; }

        /// <summary>ID of a linked TaskItem. 0 = no link.</summary>
        public int LinkedTaskId { get; set; } = 0;
        /// <summary>True when LinkedTaskId != 0 — used by XAML BoolToVis bindings.</summary>
        public bool IsLinkedToTask => LinkedTaskId != 0;

        /// <summary>Auto-parsed #hashtag tokens from clip text. Populated on capture, empty for images.</summary>
        public List<string> ParsedTags { get; set; } = new List<string>();

        // ── Image support ──────────────────────────────────────────────────────
        /// <summary>True when this entry is a captured image rather than text.</summary>
        public bool   IsImage   { get; set; }
        /// <summary>Absolute path to the saved PNG file (valid when IsImage=true).</summary>
        public string ImagePath { get; set; } = "";
        /// <summary>Convenience inverse of IsImage for XAML bindings.</summary>
        public bool   IsText    => !IsImage;

        /// <summary>Raw possibly-XAML rich text source for list-row rendering. Image clips
        /// fall back to the plain "[Image] filename" Preview so they never attempt XAML parsing.</summary>
        public string FormatSource => IsImage ? Preview : Text;

        public static ClipboardItem FromText(int id, string text)
        {
            string preview = (text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (preview.Length > 120) preview = preview.Substring(0, 117) + "…";
            return new ClipboardItem
            {
                Id         = id,
                Text       = text ?? "",
                Preview    = preview,
                CapturedAt = DateTime.Now,
                IsPinned   = false,
            };
        }

        public static ClipboardItem FromImage(int id, string imagePath)
        {
            return new ClipboardItem
            {
                Id         = id,
                Text       = "",
                Preview    = $"[Image] {System.IO.Path.GetFileName(imagePath)}",
                IsImage    = true,
                ImagePath  = imagePath,
                CapturedAt = DateTime.Now,
                IsPinned   = false,
            };
        }
    }
}
