using System.ComponentModel;

namespace NoteStation
{
    /// <summary>A user-created folder node persisted in AppPrefs.</summary>
    public class AppFolder : INotifyPropertyChanged
    {
        private string _name     = "Folder";
        private string _category = "";

        public int    Id        { get; set; }
        public int?   ParentId  { get; set; }   // null = top-level under Saved root
        public string Tab       { get; set; } = ""; // "Clipboard" | "Notes" | "Tasks"
        public int    SortOrder { get; set; }

        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category))); }
        }

        /// <summary>
        /// Hex color (e.g. "#FFD700") inherited by new notes created inside this folder.
        /// Empty = use last-used global color.
        /// </summary>
        public string DefaultNoteBackground { get; set; } = "";

        /// <summary>Emoji icon shown for this folder in the tree. Empty = default 📁.</summary>
        public string Icon { get; set; } = "";

        /// <summary>Hex accent color (e.g. "#378ADD") for the folder's icon chip. Empty = neutral.</summary>
        public string AccentColor { get; set; } = "";

        /// <summary>When true, the folder also appears in the Favorites section at the top of the tree.</summary>
        public bool IsFavorite { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
