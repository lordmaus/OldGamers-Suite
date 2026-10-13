using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NoteStation
{
    public enum FolderNodeType { Search, Recent, Pinned, SavedRoot, UserFolder, Archived, RecentlyDeleted, All, SectionHeader }

    /// <summary>TreeView view-model for a folder node (virtual or user-created).</summary>
    public class FolderNode : INotifyPropertyChanged
    {
        private string _name       = "";
        private string _category   = "";
        private string _icon       = JtIcons.DefaultFolderKey;
        private int    _count;
        private int    _countDone;
        private int    _countPending;
        private bool   _isExpanded = true;
        private bool   _isMultiSelected;
        private string _accentColor = "";
        private bool   _isRenaming;
        private bool   _isFavorite;

        public FolderNodeType NodeType  { get; set; }
        public int?           FolderId  { get; set; }   // null for virtual nodes
        public string         Tab       { get; set; } = "";

        public string Name
        {
            get => _name;
            set { _name = value; PC(nameof(Name)); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; PC(nameof(Category)); }
        }

        public string Icon
        {
            get => _icon;
            set { _icon = value; PC(nameof(Icon)); }
        }

        public int Count
        {
            get => _count;
            set { _count = value; PC(nameof(Count)); PC(nameof(CountLabel)); }
        }

        /// <summary>Tasks completed in this folder.</summary>
        public int CountDone
        {
            get => _countDone;
            set { _countDone = value; PC(nameof(CountDone)); PC(nameof(CountLabel)); }
        }

        /// <summary>Tasks pending (not completed) in this folder.</summary>
        public int CountPending
        {
            get => _countPending;
            set { _countPending = value; PC(nameof(CountPending)); PC(nameof(CountLabel)); }
        }

        /// <summary>
        /// For Tasks tab: shows "N↑ M✓"; for all other tabs: just the total count.
        /// </summary>
        public string CountLabel =>
            Tab == "Tasks"
                ? $"{_countPending}↑ {_countDone}✓"
                : _count.ToString();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PC(nameof(IsExpanded)); }
        }

        /// <summary>True when this node is part of a Ctrl+click multi-selection. Not persisted.</summary>
        public bool IsMultiSelected
        {
            get => _isMultiSelected;
            set { _isMultiSelected = value; PC(nameof(IsMultiSelected)); }
        }

        /// <summary>Hex accent color for the icon chip (user folders). Empty = neutral.</summary>
        public string AccentColor
        {
            get => _accentColor;
            set { _accentColor = value ?? ""; PC(nameof(AccentColor)); }
        }

        /// <summary>True while the node's name is being edited inline. Not persisted.</summary>
        public bool IsRenaming
        {
            get => _isRenaming;
            set { _isRenaming = value; PC(nameof(IsRenaming)); }
        }

        /// <summary>True when this folder is pinned to the Favorites section.</summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; PC(nameof(IsFavorite)); }
        }

        /// <summary>True for the collapsible section grouping headers (Favorites / Quick views / Folders).</summary>
        public bool IsSectionHeader => NodeType == FolderNodeType.SectionHeader;

        /// <summary>True for nodes that render with the muted "header" style (section headers + the Folders root).</summary>
        public bool IsHeaderStyle => NodeType == FolderNodeType.SectionHeader || NodeType == FolderNodeType.SavedRoot;

        /// <summary>True for user-created folders (shows icon chip + category pill).</summary>
        public bool IsUserFolder => NodeType == FolderNodeType.UserFolder;

        public ObservableCollection<FolderNode> Children { get; } = new();

        public event PropertyChangedEventHandler PropertyChanged;
        private void PC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
