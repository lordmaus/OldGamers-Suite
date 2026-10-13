using System;

namespace NoteStation
{
    /// <summary>
    /// A named filter preset the user can save and apply with one click.
    /// Filter expressions use space-separated key:value tokens, e.g.
    ///   "folder:Game priority:high type:task"
    ///   "tag:loadout type:clip"
    ///   "overdue type:all"
    /// Scope controls which tab is switched to when applied.
    /// </summary>
    public class SavedView
    {
        public int    Id               { get; set; }
        public string Name             { get; set; } = "";
        /// <summary>Space-separated filter tokens.</summary>
        public string FilterExpression { get; set; } = "";
        /// <summary>"All" | "Clips" | "Notes" | "Tasks"</summary>
        public string Scope            { get; set; } = "All";
        public DateTime CreatedAt      { get; set; } = DateTime.Now;
    }
}
