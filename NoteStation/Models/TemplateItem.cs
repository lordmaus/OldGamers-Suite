using System;

namespace NoteStation
{
    /// <summary>
    /// A named, reusable text snippet.  Stored separately from clipboard history,
    /// never expires, and never counts against MaxHistory.
    /// </summary>
    public class TemplateItem
    {
        public int      Id        { get; set; }
        public string   Title     { get; set; } = "";
        public string   Text      { get; set; } = "";
        public string   Category  { get; set; } = "";
        public int?     FolderId  { get; set; }
        public int      SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
    }
}
